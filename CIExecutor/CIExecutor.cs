namespace CIExecutor;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix;
using RevolutionaryWebApp.Server.Common.Models;
using RevolutionaryWebApp.Server.Common.Utilities;
using RevolutionaryWebApp.Shared;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using RevolutionaryWebApp.Shared.Utilities;
using SharedBase.Utilities;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public sealed class CIExecutor : IDisposable
{
    private const bool VerboseSectionFail = false;

    private const int TargetOutputSingleMessageSize = 2500;
    private const int QueueLargeThreshold = 3;
    private const string OutputSpecialCommandMarker = "#--@%-DevCenter-%@--";
    private const string LineTruncateMessage = " THIS LINE WAS TRUNCATED BECAUSE IT IS TOO LONG";

    private readonly string websocketUrl;
    private readonly ReaderWriterLock protocolSocketLock = new();

    private readonly string imageCacheFolder;
    private readonly string ciImageFile;
    private readonly string ciImageName;
    private readonly string localBranch;
    private readonly string ciJobName;

    private readonly bool printBuildCommands = false;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly bool isSafe;

    private readonly string cacheBaseFolder;
    private readonly string sharedCacheFolder;
    private readonly string jobCacheBaseFolder;
    private readonly string ciRef;
    private readonly string defaultBranch;

    private readonly List<RealTimeBuildMessage> queuedBuildMessages = new();

    private ClientWebSocket? webSocket;
    private RealTimeBuildMessageSocket? protocolSocket;
    private CiJobCacheConfiguration? cacheConfig;

    private string? currentBuildRootFolder;

    private bool running;
    private bool failure;
    private bool buildCommandsFailed;

    private bool lastSectionClosed = true;
    private string? ciCommit;

    public CIExecutor(string websocketUrl)
    {
        Console.WriteLine("Parsing variables from env");
        this.websocketUrl = websocketUrl.Replace("https://", "wss://").Replace("http://", "ws://");

        // TODO: detect the actual username as a fallback here
        var home = Environment.GetEnvironmentVariable("HOME") ?? "/home/rocky";

        imageCacheFolder = Path.Join(home, "images");
        ciImageFile = Path.Join(imageCacheFolder, Environment.GetEnvironmentVariable("CI_IMAGE_FILENAME"));
        ciImageName = Environment.GetEnvironmentVariable("CI_IMAGE_NAME") ??
            throw new Exception("Missing environment variable 'CI_IMAGE_NAME'");
        localBranch = Environment.GetEnvironmentVariable("CI_BRANCH") ??
            throw new Exception("Missing environment variable 'CI_BRANCH'");
        ciJobName = Environment.GetEnvironmentVariable("CI_JOB_NAME") ??
            throw new Exception("Missing environment variable 'CI_JOB_NAME'");
        ciRef = Environment.GetEnvironmentVariable("CI_REF") ??
            throw new Exception("Missing environment variable 'CI_REF'");
        defaultBranch = Environment.GetEnvironmentVariable("CI_DEFAULT_BRANCH") ??
            throw new Exception("Missing environment variable 'CI_DEFAULT_BRANCH'");

        isSafe = Convert.ToBoolean(Environment.GetEnvironmentVariable("CI_TRUSTED"));

        cacheBaseFolder = isSafe ? "/executor_cache/safe" : "/executor_cache/unsafe";
        sharedCacheFolder = Path.Join(cacheBaseFolder, "shared");
        jobCacheBaseFolder = Path.Join(cacheBaseFolder, "named");
    }

    private bool Failure
    {
        get => failure;
        set
        {
            if (failure == value)
                return;

            if (value == false)
                throw new ArgumentException("Can't set failure to false");

            Console.WriteLine("Setting Failure to true");

            failure = true;
            QueueSendMessage(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.FinalStatus,
                WasSuccessful = false,
            }).Wait();
        }
    }

    public async Task Run()
    {
        Console.WriteLine("CI executor starting");
        running = true;

        Console.WriteLine("Starting websocket");
        var connectTask = AcquireWebsocketConnection();

        await QueueSendMessage(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.SectionStart,
            SectionName = "Environment setup",
        });
        lastSectionClosed = false;

        Console.WriteLine("Going to parse cache options");
        try
        {
            cacheConfig = JsonSerializer.Deserialize<CiJobCacheConfiguration>(
                Environment.GetEnvironmentVariable("CI_CACHE_OPTIONS") ??
                throw new Exception("environment variable for cache not set"));

            if (cacheConfig == null)
                throw new Exception("Loaded cache config is null");
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed to load cache config: {0}", e);
            await EndSectionWithFailure("Failed to read cache configuration");
        }

        if (!Failure)
            await SetupCaches();

        if (!Failure)
            await SetupRepo();

        // Wait for connection before continuing from the git setup
        await connectTask;

        // Start socket related tasks
        var processMessagesTask = Task.Run(ProcessBuildMessages);

        var cancelRead = new CancellationTokenSource();

        // ReSharper disable once MethodSupportsCancellation
        var readMessagesTask = Task.Run(() => ReadSocketMessages(cancelRead.Token));

        if (!Failure)
            await SetupImages();

        if (!Failure)
            await RunBuild();

        if (!Failure)
            await RunPostBuild();

        Console.WriteLine("Reached shutdown");
        running = false;

        Console.WriteLine("Waiting for message sending");
        try
        {
            await processMessagesTask;
        }
        catch (Exception e)
        {
            Console.WriteLine("Got an exception when waiting for message send task: {0}", e);
        }

        Console.WriteLine("Waiting 1s before closing socket read");

        // ReSharper disable once MethodSupportsCancellation
        await Task.Delay(TimeSpan.FromSeconds(1));

        cancelRead.Cancel();

        Console.WriteLine("Waiting for incoming messages");
        try
        {
            await readMessagesTask;
        }
        catch (Exception e)
        {
            Console.WriteLine("Got an exception when waiting for message read task: {0}", e);
        }

        Console.WriteLine("Closing socket");

        try
        {
            if (webSocket == null)
                throw new Exception("Web socket object is null");

            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            webSocket.Dispose();
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed to close socket: {0}", e);
        }

        Console.WriteLine("CI executor finished");
    }

    public void Dispose()
    {
        Console.WriteLine("Disposing CI executor");
        webSocket?.Dispose();
    }

    private static void AddSecretsConfiguration(List<CiSecretExecutorData> secrets, List<string> arguments)
    {
        foreach (var secret in secrets)
        {
            arguments.Add("-e");

            // Quoting cannot be used with podman (and also not with docker) so we can't do that here, hopefully
            // this is safe enough like this
            arguments.Add($"{secret.SecretName}={secret.SecretContent}");
        }
    }

    private async Task AcquireWebsocketConnection()
    {
        Console.WriteLine("Starting socket acquire");
        webSocket = new ClientWebSocket();

        // This is now the same as on the server, hopefully causes less issues
        webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(60);
        await webSocket.ConnectAsync(new Uri(websocketUrl), CancellationToken.None);
        Console.WriteLine("Websocket opened");

        protocolSocketLock.AcquireWriterLock(TimeSpan.FromSeconds(15));
        try
        {
            protocolSocket = new RealTimeBuildMessageSocket(webSocket);
            Console.WriteLine("Set protocol socket");
        }
        finally
        {
            protocolSocketLock.ReleaseWriterLock();
        }
    }

    private async Task QueueSendMessage(RealTimeBuildMessage message)
    {
        if (lastSectionClosed && message.Type == BuildSectionMessageType.BuildOutput)
        {
            Console.WriteLine("Ignoring build message to be sent as there's no active section: {0}",
                message.Output);
            Console.WriteLine(Environment.StackTrace);
            return;
        }

        int waitTime;

        lock (queuedBuildMessages)
        {
            // Merge messages
            if (message.Type == BuildSectionMessageType.BuildOutput)
            {
                var last = queuedBuildMessages.LastOrDefault();

                if (last != null && last.Type == message.Type &&
                    last.Output!.Length + message.Output!.Length < TargetOutputSingleMessageSize)
                {
                    // Messages can be merged for sending them together
                    last.Output += message.Output;
                    return;
                }
            }

            queuedBuildMessages.Add(message);

            waitTime = queuedBuildMessages.Count / QueueLargeThreshold;
        }

        // Try to sleep some time to give the message sender task some time to send stuff away
        if (waitTime > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(3 * waitTime));
    }

    private async Task ProcessBuildMessages()
    {
        var toSend = new List<RealTimeBuildMessage>();

        bool sleep = false;

        var lastSend = DateTime.Now;

        while (true)
        {
            if (sleep)
                await Task.Delay(TimeSpan.FromSeconds(1));

            sleep = true;

            lock (queuedBuildMessages)
            {
                if (queuedBuildMessages.Count < 1)
                {
                    if (!running)
                        break;
                }
                else
                {
                    sleep = false;

                    toSend.AddRange(queuedBuildMessages);
                    queuedBuildMessages.Clear();
                }
            }

            if (sleep)
            {
                // Send some text to keep the build socket open?
                // Actually the download image step has taken over 10 minutes with no output in the past, so
                // this can't actually be a fix. It must be that sometimes jetbrains build step dies for some
                // other reason
                if (DateTime.Now - lastSend > TimeSpan.FromSeconds(50))
                {
                    try
                    {
                        protocolSocketLock.AcquireReaderLock(TimeSpan.FromSeconds(30));

                        try
                        {
                            if (protocolSocket == null)
                                throw new Exception("protocolSocket has been destroyed");

                            await protocolSocket.Write(new RealTimeBuildMessage
                                    { Type = BuildSectionMessageType.BuildOutput, Output = "..." },
                                CancellationToken.None);
                        }
                        finally
                        {
                            protocolSocketLock.ReleaseReaderLock();
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Socket dots keepalive message send exception: {0}", e);
                    }

                    lastSend = DateTime.Now;
                }

                continue;
            }

            try
            {
                protocolSocketLock.AcquireReaderLock(TimeSpan.FromSeconds(30));

                try
                {
                    if (protocolSocket == null)
                        throw new Exception("protocolSocket has been destroyed");

                    foreach (var message in toSend)
                        await protocolSocket.Write(message, CancellationToken.None);
                }
                finally
                {
                    protocolSocketLock.ReleaseReaderLock();
                }
            }
            catch (Exception e)
            {
                sleep = true;
                Console.WriteLine("Socket message send exception: {0}, inner: {1}", e, e.InnerException);
            }

            lastSend = DateTime.Now;
            toSend.Clear();
        }
    }

    private async Task ReadSocketMessages(CancellationToken cancellationToken)
    {
        while (running)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            (RealTimeBuildMessage? Message, bool Closed) received;

            bool lockFailed = false;

            protocolSocketLock.AcquireReaderLock(TimeSpan.FromSeconds(1500));
            try
            {
                if (protocolSocket == null)
                    throw new Exception("protocolSocket has been destroyed");

                received = await protocolSocket.Read(cancellationToken);
            }
            catch (WebSocketProtocolException e)
            {
                Console.WriteLine("Socket read exception: {0}, inner: {1}", e, e.InnerException);

                // TODO: perform socket re-opening here

                break;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Socket read canceled");
                break;
            }
            finally
            {
                try
                {
                    protocolSocketLock.ReleaseReaderLock();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to release socket reader lock: {0}", e);
                    lockFailed = true;
                }
            }

            if (lockFailed)
                return;

            if (received.Closed)
            {
                Console.WriteLine("Remote side closed the socket while we were reading");
                break;
            }

            if (received.Message != null)
            {
                Console.WriteLine("Received message from server: {0}, {1}, {2}", received.Message.Type,
                    received.Message.ErrorMessage, received.Message.Output);
            }
        }
    }

    private async Task SetupCaches()
    {
        Console.WriteLine("Starting cache setup");
        await QueueSendBasicMessage("Starting cache setup");

        try
        {
            Directory.CreateDirectory(cacheBaseFolder);
            Directory.CreateDirectory(sharedCacheFolder);
            Directory.CreateDirectory(imageCacheFolder);

            currentBuildRootFolder = Path.Join(jobCacheBaseFolder, HandleCacheTemplates(cacheConfig!.WriteTo));

            var cacheCopyFromFolders =
                cacheConfig.LoadFrom.Select(p => Path.Join(jobCacheBaseFolder, HandleCacheTemplates(p))).ToList();

            if (!Directory.Exists(currentBuildRootFolder))
            {
                await QueueSendBasicMessage($"Cache folder doesn't exist yet ({currentBuildRootFolder})");

                bool cacheCopied = false;

                foreach (var cachePath in cacheCopyFromFolders)
                {
                    Console.WriteLine($"Checking cache copy from folder: {cachePath}");
                    if (!Directory.Exists(cachePath))
                        continue;

                    if (!await RunWithOutputStreaming("cp", new List<string>
                        {
                            "-aT", cachePath, currentBuildRootFolder,
                        }))
                    {
                        throw new Exception("Failed to run cache copy command");
                    }

                    await QueueSendBasicMessage($"Initializing cache with copy from: {cachePath}");
                    cacheCopied = true;
                    break;
                }

                if (!cacheCopied)
                {
                    await QueueSendBasicMessage("No existing cache found to copy from " +
                        $"(last checked cache: {cacheCopyFromFolders.LastOrDefault()}");
                }
            }
            else
            {
                await QueueSendBasicMessage($"Base build folder already exists at {currentBuildRootFolder}");
            }
        }
        catch (Exception e)
        {
            await EndSectionWithFailure($"Error setting up caches: {e}");
        }

        await QueueSendBasicMessage("Cache setup finished");
    }

    private async Task SetupRepo()
    {
        Console.WriteLine("Starting repo setup");

        try
        {
            ciCommit = Environment.GetEnvironmentVariable("CI_COMMIT_HASH") ??
                throw new Exception("Missing environment variable 'CI_COMMIT_HASH'");
            var ciOrigin = Environment.GetEnvironmentVariable("CI_ORIGIN") ??
                throw new Exception("Missing environment variable 'CI_ORIGIN'");

            await QueueSendBasicMessage($"Checking out the needed ref: {ciRef} and commit: {ciCommit}");

            var folder = currentBuildRootFolder ?? throw new Exception("build root folder not set");

            // LFS is skipped here as this has caused a lot of extra bandwidth
            await GitRunHelpers.EnsureRepoIsCloned(ciOrigin, folder, true, CancellationToken.None);

            // Fetch the ref
            await QueueSendBasicMessage($"Fetching the ref: {ciRef}");
            await GitRunHelpers.FetchRef(folder, ciRef, CancellationToken.None);

            // Also fetch the default branch for comparing changes against it
            await GitRunHelpers.Fetch(folder, defaultBranch, ciOrigin, CancellationToken.None);

            await GitRunHelpers.Checkout(folder, ciCommit, true, CancellationToken.None, true);
            await QueueSendBasicMessage($"Checked out commit {ciCommit}");

            await GitRunHelpers.UpdateSubmodules(folder, true, true, CancellationToken.None);
            await QueueSendBasicMessage("Submodules are up to date");

            Dictionary<string, string> notAppliedCaches = new Dictionary<string, string>();

            // Early set up any LFS (git) related caches to save on bandwidth
            if (cacheConfig!.Shared != null)
            {
                foreach (var tuple in cacheConfig.Shared)
                {
                    var source = tuple.Key;
                    var destination = tuple.Value;

                    var fullSource = Path.Join(currentBuildRootFolder, source);
                    var fullDestination = Path.Join(sharedCacheFolder, destination);

                    if (!source.StartsWith(".git"))
                    {
                        // Something to handle later
                        notAppliedCaches[fullSource] = fullDestination;
                        continue;
                    }

                    await QueueSendBasicMessage($"Early handling git cache {source} to {destination}");
                    await HandleSharedCache(fullSource, fullDestination, destination);
                }
            }

            // And only now pull the LFS
            var timer = new Stopwatch();
            timer.Start();
            await GitRunHelpers.LfsPull(folder, CancellationToken.None);
            await QueueSendBasicMessage($"LFS file pull completed in {timer.Elapsed}");

            // Clean out non-ignored files
            var deleted = await GitRunHelpers.Clean(folder, CancellationToken.None);

            await QueueSendBasicMessage($"Cleaned non-ignored extra files: {deleted}");

            // Handling of shared cache paths with symlinks
            foreach (var tuple in notAppliedCaches)
            {
                var fullSource = tuple.Key;
                var fullDestination = tuple.Value;

                await HandleSharedCache(fullSource, fullDestination, Path.GetFileName(fullDestination));
            }

            await QueueSendBasicMessage("Repository checked out");
        }
        catch (Exception e)
        {
            await EndSectionWithFailure($"Error cloning / checking out: {e}");
        }
    }

    private async Task HandleSharedCache(string fullSource, string fullDestination, string destination)
    {
        // TODO: is a separate handling needed for when the fullSource is a single file and
        // not a directory?

        var isAlreadySymlink = Directory.Exists(fullSource) &&
            new UnixSymbolicLinkInfo(fullSource).IsSymbolicLink;

        if (Directory.Exists(fullSource) && !Directory.Exists(fullDestination) && !isAlreadySymlink)
        {
            await QueueSendBasicMessage($"Using existing folder to create shared cache {destination}");
            Directory.Move(fullSource, fullDestination);
        }

        if (!Directory.Exists(fullDestination))
        {
            await QueueSendBasicMessage($"Creating new shared cache {destination}");
            Directory.CreateDirectory(fullDestination);
        }

        if (isAlreadySymlink)
            return;

        if (Directory.Exists(fullSource))
        {
            await QueueSendBasicMessage(
                $"Deleting existing directory {Path.GetFileName(fullSource)} to link to shared cache {destination}");
            Directory.Delete(fullSource, true);
        }

        // Make sure the folder we are going to create the symbolic link in exists
        Directory.CreateDirectory(PathParser.GetParentPath(fullSource));

        await QueueSendBasicMessage($"Using shared cache {destination}");
        new UnixSymbolicLinkInfo(fullSource).CreateSymbolicLinkTo(fullDestination);
    }

    private async Task SetupImages()
    {
        Console.WriteLine("Starting image setup");
        await QueueSendBasicMessage($"Using build environment image: {ciImageName}");

        try
        {
            await QueueSendBasicMessage($"Storing images in {imageCacheFolder}");

            // Check if podman already has the image, if it exists, we don't need to do anything further

            var startInfo = new ProcessStartInfo("podman")
            {
                CreateNoWindow = true,
                ArgumentList = { "images" },
            };

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, CancellationToken.None);
            if (result.ExitCode != 0)
                throw new Exception($"Failed to check existing images from podman: {result.FullOutput}");

            var imageNameParts = ciImageName.Split(':');

            if (imageNameParts.Length != 2)
                throw new Exception("Image name part expected to have two parts separated by ':'");

            var existingImageLine = result.Output.Split('\n')
                .FirstOrDefault(l => l.Contains(imageNameParts[0]) && l.Contains(imageNameParts[1]));

            if (existingImageLine != null)
            {
                await QueueSendBasicMessage($"Build environment image already exists: {existingImageLine}");
            }
            else
            {
                await LoadBuildImageToPodman();
                await QueueSendBasicMessage("Build environment image loaded");
            }

            await DeleteBuildImageDownload();

            await QueueSendMessage(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.SectionEnd,
                WasSuccessful = true,
            });
            lastSectionClosed = true;
        }
        catch (Exception e)
        {
            await EndSectionWithFailure($"Error handling build image: {e}");
        }
    }

    private async Task LoadBuildImageToPodman()
    {
        if (!File.Exists(ciImageFile))
        {
            await DownloadBuildImage();
        }

        if (!await RunWithOutputStreaming("podman", new List<string> { "load", "-i", ciImageFile }))
        {
            // Delete a bad download
            try
            {
                File.Delete(ciImageFile);
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to delete invalid image file: {0}", e);
            }

            throw new Exception("Failed to load the image file");
        }
    }

    private async Task DownloadBuildImage()
    {
        Directory.CreateDirectory(PathParser.GetParentPath(ciImageFile));

        await QueueSendBasicMessage("Build environment image doesn't exist locally, downloading...");

        var downloadUrl = Environment.GetEnvironmentVariable("CI_IMAGE_DL_URL") ??
            throw new Exception("Missing environment variable 'CI_IMAGE_DL_URL'");

        if (!await RunWithOutputStreaming("curl", new List<string>
            {
                "-LsS", downloadUrl, "--output", ciImageFile,
            }))
        {
            // Delete a partial download
            try
            {
                File.Delete(ciImageFile);
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to delete failed image download: {0}", e);
            }

            throw new Exception("Failed to download image file");
        }
    }

    private async Task DeleteBuildImageDownload()
    {
        if (!File.Exists(ciImageFile))
            return;

        try
        {
            File.Delete(ciImageFile);
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed to delete already loaded build image file: {0}", e);
            await QueueSendBasicMessage("Error: could not delete on-disk downloaded build image to save space");
        }
    }

    private async Task RunBuild()
    {
        Console.WriteLine("Starting build");
        try
        {
            await QueueSendMessage(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.SectionStart,
                SectionName = "Build start",
            });
            lastSectionClosed = false;

            await QueueSendBasicMessage($"Using build environment image: {ciImageName}");

            var folder = currentBuildRootFolder ?? throw new Exception("build root folder not set");
            var buildConfig = await LoadCIBuildConfiguration(folder);

            if (buildConfig == null)
                return;

            var command = BuildCommandsFromBuildConfig(buildConfig, ciJobName, folder);

            if (command == null || command.Count < 1)
                throw new Exception("Failed to parse CI configuration to build list of build commands");

            if (printBuildCommands)
                PrintBuildCommands(command);

            List<CiSecretExecutorData>? secrets;
            try
            {
                secrets = JsonSerializer.Deserialize<List<CiSecretExecutorData>>(
                    Environment.GetEnvironmentVariable("CI_SECRETS") ??
                    throw new Exception("environment variable for secrets not set"));

                if (secrets == null)
                    throw new Exception("parsed secrets list is empty");
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to load build secrets: {0}", e);
                throw new Exception("Failed to read build secrets");
            }

            lastSectionClosed = false;

            var runArguments = new List<string>
            {
                // A little bit lower priority
                "-n", "4",
                "podman", "run", "--rm", "-i", "-e", $"CI_REF={ciRef}", "-e", $"CI_BRANCH={localBranch}",
                "-e", $"CI_COMMIT_HASH={ciCommit}",
                "-e", $"CI_EARLIER_COMMIT={Environment.GetEnvironmentVariable("CI_EARLIER_COMMIT")}",
                "-e", $"CI_DEFAULT_BRANCH={defaultBranch}",
            };

            AddMountConfiguration(runArguments);

            // We pass only the specific environment variables to the container
            AddSecretsConfiguration(secrets, runArguments);

            runArguments.Add(ciImageName);
            runArguments.Add("/bin/bash");

            Console.WriteLine("Running podman build");
            var result = await RunWithInputAndOutput(command, "nice", runArguments);
            Console.WriteLine("Process finished: {0}", result);

            if (!result)
                buildCommandsFailed = true;

            if (!lastSectionClosed)
            {
                // TODO: probably would be nice to print this message anyway even if the last section is closed...
                await QueueSendBasicMessage(result ? "Build commands succeeded" : "Build commands failed");

                await QueueSendMessage(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.SectionEnd,
                    WasSuccessful = result,
                });
                lastSectionClosed = true;
            }
        }
        catch (Exception e)
        {
            await EndSectionWithFailure($"Error running build commands: {e}");
        }
    }

    private async Task RunPostBuild()
    {
        Console.WriteLine("Starting post-build");

        // TODO: build artifacts

        // Send final status
        Console.WriteLine("Sending final status: {0}", !buildCommandsFailed);
        await QueueSendMessage(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.FinalStatus,
            WasSuccessful = !Failure && !buildCommandsFailed,
        });
    }

    private Task QueueSendBasicMessage(string message)
    {
        return QueueSendMessage(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.BuildOutput,
            Output = $"{message}\n",
        });
    }

    private async Task EndSectionWithFailure(string error)
    {
        Console.WriteLine("Failing current section with error: {0}", error);
        await QueueSendBasicMessage(error);

        await QueueSendMessage(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.SectionEnd,
            WasSuccessful = false,
        });
        lastSectionClosed = true;

        Failure = true;
    }

    private string HandleCacheTemplates(string cachePath)
    {
        return cachePath.Replace("{Branch}", localBranch).Replace("/", "-");
    }

    private void AddMountConfiguration(List<string> arguments)
    {
        arguments.Add("--mount");
        arguments.Add($"type=bind,source={currentBuildRootFolder},destination={currentBuildRootFolder},relabel=shared");
        arguments.Add("--mount");
        arguments.Add($"type=bind,source={sharedCacheFolder},destination={sharedCacheFolder},relabel=shared");
    }

    private async Task<CiBuildConfiguration?> LoadCIBuildConfiguration(string folder)
    {
        try
        {
            var text = await File.ReadAllTextAsync(Path.Join(folder, AppInfo.CIConfigurationFile), Encoding.UTF8);

            var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var configuration = deserializer.Deserialize<CiBuildConfiguration>(text);

            if (configuration == null)
                throw new Exception("Deserialized is null");

            // We don't verify the model here, as it should have been verified by the job configuration creation
            // already

            return configuration;
        }
        catch (Exception e)
        {
            Console.WriteLine("Error reading build configuration file: {0}", e);
            await EndSectionWithFailure("Error reading or parsing build configuration file");
            return null;
        }
    }

    private List<string>? BuildCommandsFromBuildConfig(CiBuildConfiguration configuration, string jobName,
        string folder)
    {
        if (!configuration.Jobs.TryGetValue(jobName, out CiJobConfiguration? config))
        {
            QueueSendBasicMessage($"Config file is missing current job: {jobName}");
            return null;
        }

        // Startup part
        var command = new List<string>
        {
            "echo 'Starting running build in container'",
        };

        if (config.Cache.System != null)
        {
            // Setup the system cache redirects
            foreach (var systemCacheEntry in config.Cache.System)
            {
                if (!systemCacheEntry.Key.StartsWith("/root"))
                {
                    command.Add("echo 'Ignored system cache that doesn't begin with \"/root\"'");
                    continue;
                }

                command.Add($"echo 'Setting up system cache folder \"{systemCacheEntry.Key}\" to " +
                    $"point to \"{systemCacheEntry.Value}\"'");

                var cacheTarget = Path.Join(sharedCacheFolder, systemCacheEntry.Value);
                var linkParentFolder = Path.GetDirectoryName(systemCacheEntry.Key);

                command.Add($"rm -rf '{systemCacheEntry.Key}'");
                command.Add($"mkdir -p '{cacheTarget}'");
                command.Add($"mkdir -p '{linkParentFolder}'");
                command.Add($"ln -sf '{cacheTarget}' '{systemCacheEntry.Key}' || " +
                    "{ echo \"Couldn't link system cache folder\"; exit 1; }");
                command.Add("echo 'Finished system cache folder linking'");
            }
        }

        command.Add($"cd '{folder}' || {{ echo \"Couldn't switch to build folder\"; exit 1; }}");
        command.Add("echo 'Starting build commands'");
        command.Add($"echo '{OutputSpecialCommandMarker} SectionEnd 0'");
        command.Add("overallStatus=0");

        // Build commands
        foreach (var step in config.Steps)
        {
            if (step.Run == null)
                continue;

            var name = string.IsNullOrEmpty(step.Run.Name) ?
                step.Run.Command.Truncate(70) :
                step.Run.Name;

            if (step.Run.When == CiJobStepRunCondition.Always)
            {
                command.Add("if [ 1 = 1 ]; then");
            }
            else if (step.Run.When == CiJobStepRunCondition.Failure)
            {
                command.Add("if [ ! $overallStatus -eq 0 ]; then");
            }
            else
            {
                // Run on previous being successful
                command.Add("if [ $overallStatus -eq 0 ]; then");
            }

            command.Add($"echo \"{OutputSpecialCommandMarker} SectionStart {BashEscape.EscapeForBash(name)}\"");

            // Step is ran in subshell
            command.Add("(");
            command.Add("set -e");

            foreach (var line in step.Run.Command.Split('\n'))
            {
                command.Add(line);
            }

            command.Add(")");

            command.Add("lastStatus=$?");
            command.Add("if [ ! $lastStatus -eq 0 ]; then");

            // ReSharper disable once HeuristicUnreachableCode
#pragma warning disable CS0162
            if (VerboseSectionFail)
                command.Add("echo Running this section failed");
#pragma warning restore CS0162

            command.Add("overallStatus=1");
            command.Add("fi");
            command.Add($"echo \"{OutputSpecialCommandMarker} SectionEnd $lastStatus\"");
            command.Add("fi");
        }

        command.Add("exit 0");

        return command;
    }

    private void PrintBuildCommands(List<string> commands)
    {
        Console.WriteLine("Build commands:");
        foreach (var command in commands)
        {
            Console.WriteLine(command);
        }

        Console.WriteLine("end of build commands");
    }

    private string ProcessBuildOutputLine(string line)
    {
        if (line.Length > AppInfo.MaxBuildOutputLineLength)
        {
            var truncated = line.Substring(0, AppInfo.MaxBuildOutputLineLength - LineTruncateMessage.Length);
            return $"{truncated}{LineTruncateMessage}\n";
        }

        return line + "\n";
    }

    private async Task<bool> RunWithOutputStreaming(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable) { CreateNoWindow = true };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        void SendOutputMessage(string line)
        {
            QueueSendMessage(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.BuildOutput,
                Output = ProcessBuildOutputLine(line),
            }).Wait();
        }

        var result = await ProcessRunHelpers.RunProcessWithOutputStreamingAsync(startInfo, CancellationToken.None,
            SendOutputMessage, SendOutputMessage);

        if (result.ExitCode != 0)
        {
            if (result.ExitCode == ProcessRunHelpers.EXIT_STATUS_UNAVAILABLE)
                Console.WriteLine("Failed to read process result code");

            Console.WriteLine("Failed to run: {0}", executable);
            return false;
        }

        return true;
    }

    private async Task<bool> RunWithInputAndOutput(List<string> inputLines, string executable,
        IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable) { CreateNoWindow = true };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        void JustSendOutputMessage(string line)
        {
            QueueSendMessage(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.BuildOutput,
                Output = ProcessBuildOutputLine(line),
            }).Wait();
        }

        void HandleOutputLine(string line)
        {
            if (!line.StartsWith(OutputSpecialCommandMarker))
            {
                // Normal output
                JustSendOutputMessage(line);
                return;
            }

            // A special command
            // TODO: could refactor this to split at most into 3 parts as the last part can be arbitrary text we
            // don't need to parse
            var parts = line.Split(' ');

            switch (parts[1])
            {
                case "SectionEnd":
                {
                    var success = Convert.ToInt32(parts[2]) == 0;

                    QueueSendMessage(new RealTimeBuildMessage
                    {
                        Type = BuildSectionMessageType.SectionEnd,
                        WasSuccessful = success,
                    }).Wait();

                    if (!success)
                        buildCommandsFailed = true;

                    lastSectionClosed = true;
                    break;
                }

                case "SectionStart":
                {
                    QueueSendMessage(new RealTimeBuildMessage
                    {
                        Type = BuildSectionMessageType.SectionStart,
                        SectionName = string.Join(' ', parts.Skip(2)),
                    }).Wait();
                    lastSectionClosed = false;

                    break;
                }

                default:
                {
                    EndSectionWithFailure("Unknown special command received from build process").Wait();
                    break;
                }
            }
        }

        var result = await ProcessRunHelpers.RunProcessWithStdInAndOutputStreamingAsync(startInfo,
            CancellationToken.None, inputLines, HandleOutputLine, JustSendOutputMessage);

        if (!result.AllInputLinesWritten)
            Console.WriteLine("Process exited before all input lines were written");

        if (result.ErrorInInputLineClosing)
            Console.WriteLine("Failed to close input stream after writing input");

        if (result.ExitCode != 0)
        {
            if (result.ExitCode == ProcessRunHelpers.EXIT_STATUS_UNAVAILABLE)
                Console.WriteLine("Failed to read process result code");

            Console.WriteLine("Failed to run: {0}", executable);
            return false;
        }

        return true;
    }
}
