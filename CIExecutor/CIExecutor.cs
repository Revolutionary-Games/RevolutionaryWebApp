namespace CIExecutor
{
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
    using ThriveDevCenter.Server.Common.Models;
    using ThriveDevCenter.Server.Common.Utilities;
    using ThriveDevCenter.Shared;
    using ThriveDevCenter.Shared.Models;
    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    public class CIExecutor
    {
        private const int TargetOutputSingleMessageSize = 4960;
        private const string OutputSpecialCommandMarker = "#--@%-DevCenter-%@--";

        private readonly string websocketUrl;

        private readonly string imageCacheFolder;
        private readonly string ciImageFile;
        private readonly string localBranch;
        private readonly string ciJobName;
        private readonly bool isSafe;
        private readonly string cacheBaseFolder;
        private readonly string sharedCacheFolder;
        private readonly string jobCacheBaseFolder;

        private readonly string podmanPath;

        private readonly List<RealTimeBuildMessage> queuedBuildMessages = new();

        private RealTimeBuildMessageSocket protocolSocket;
        private CiJobCacheConfiguration cacheConfig;

        private string currentBuildRootFolder;

        private bool running;
        private bool failure;

        private bool lastSectionClosed = true;

        public CIExecutor(string websocketUrl)
        {
            this.websocketUrl = websocketUrl.Replace("https://", "wss://").Replace("http://", "ws://");

            var home = Environment.GetEnvironmentVariable("HOME") ?? "/home/centos";

            imageCacheFolder = Path.Join(home, "images");
            ciImageFile = Path.Join(imageCacheFolder, Environment.GetEnvironmentVariable("CI_IMAGE_FILENAME"));
            localBranch = Environment.GetEnvironmentVariable("CI_BRANCH");
            ciJobName = Environment.GetEnvironmentVariable("CI_JOB_NAME");

            isSafe = Convert.ToBoolean(Environment.GetEnvironmentVariable("CI_TRUSTED"));

            cacheBaseFolder = isSafe ? "/executor_cache/safe" : "executor_cache/unsafe";
            sharedCacheFolder = Path.Join(cacheBaseFolder, "shared");
            jobCacheBaseFolder = Path.Join(cacheBaseFolder, "named");

            // podmanPath = ExecutableFinder.Which("podman");
            // Let's see if this works without the full path
            podmanPath = "podman";
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

                failure = true;
                QueueSendMessage(new RealTimeBuildMessage()
                {
                    Type = BuildSectionMessageType.FinalStatus,
                    WasSuccessful = false,
                });
            }
        }

        public async Task Run()
        {
            Console.WriteLine("CI executor starting");
            running = true;

            Console.WriteLine("Starting websocket");
            var websocket = new ClientWebSocket();
            websocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(55);
            var connectTask = websocket.ConnectAsync(new Uri(websocketUrl), CancellationToken.None);

            QueueSendMessage(new RealTimeBuildMessage()
            {
                Type = BuildSectionMessageType.SectionStart,
                SectionName = "Environment setup",
            });

            Console.WriteLine("Going to parse cache options");
            try
            {
                cacheConfig = JsonSerializer.Deserialize<CiJobCacheConfiguration>(
                    Environment.GetEnvironmentVariable("CI_CACHE_OPTIONS") ??
                    throw new Exception("environment variable for cache not set"));
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to load cache config: {0}", e);
                EndSectionWithFailure("Failed to read cache configuration");
            }

            if (!Failure)
                await SetupCaches();

            if (!Failure)
                await SetupRepo();

            // Wait for connection before continuing from the git setup
            await connectTask;
            protocolSocket = new RealTimeBuildMessageSocket(websocket);

            // Start socket related tasks
            var processMessagesTask = ProcessBuildMessages();
            var readMessagesTask = ReadSocketMessages();

            if (!Failure)
                await SetupImages();

            if (!Failure)
                await RunBuild();

            if (!Failure)
                await RunPostBuild();

            running = false;
            await processMessagesTask;
            await readMessagesTask;
            await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }

        private void QueueSendMessage(RealTimeBuildMessage message)
        {
            lock (queuedBuildMessages)
            {
                // Merge messages
                if (message.Type == BuildSectionMessageType.BuildOutput)
                {
                    var last = queuedBuildMessages.LastOrDefault();

                    if (last != null && last.Type == message.Type &&
                        last.Output.Length + message.Output.Length < TargetOutputSingleMessageSize)
                    {
                        // Messages can be merged for sending them together
                        last.Output += message.Output;
                        return;
                    }
                }

                queuedBuildMessages.Add(message);
            }
        }

        private async Task ProcessBuildMessages()
        {
            var tasks = new List<Task>();

            bool sleep = false;

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

                        continue;
                    }

                    sleep = false;

                    foreach (var message in queuedBuildMessages)
                        tasks.Add(protocolSocket.Write(message));

                    queuedBuildMessages.Clear();
                }

                await Task.WhenAll(tasks);
                tasks.Clear();
            }
        }

        private async Task ReadSocketMessages()
        {
            while (running)
            {
                (RealTimeBuildMessage message, bool closed) received;
                try
                {
                    received = await protocolSocket.Read();
                }
                catch (WebSocketProtocolException e)
                {
                    Console.WriteLine("Socket read exception: {0}", e);
                    break;
                }

                if (received.closed)
                {
                    Console.WriteLine("Remote side closed the socket while we were reading");
                    break;
                }

                if (received.message != null)
                {
                    Console.WriteLine("Received message from server: {0}, {1}, {2}", received.message.Type,
                        received.message.ErrorMessage, received.message.Output);
                }
            }
        }

        private async Task SetupCaches()
        {
            QueueSendBasicMessage("Starting cache setup");

            try
            {
                Directory.CreateDirectory(cacheBaseFolder);
                Directory.CreateDirectory(sharedCacheFolder);
                Directory.CreateDirectory(imageCacheFolder);

                currentBuildRootFolder = Path.Join(jobCacheBaseFolder, HandleCacheTemplates(cacheConfig.WriteTo));

                var cacheCopyFromFolders =
                    cacheConfig.LoadFrom.Select(p => Path.Join(jobCacheBaseFolder, HandleCacheTemplates(p))).ToList();

                if (!Directory.Exists(currentBuildRootFolder))
                {
                    QueueSendBasicMessage($"Cache folder doesn't exist yet ({currentBuildRootFolder})");

                    foreach (var cachePath in cacheCopyFromFolders)
                    {
                        if (!Directory.Exists(cachePath))
                            continue;

                        await RunWithOutputStreaming("cp", new List<string>
                        {
                            "-aT", cachePath, currentBuildRootFolder,
                        });
                    }
                }
            }
            catch (Exception e)
            {
                EndSectionWithFailure($"Error setting up caches: {e}");
            }

            QueueSendBasicMessage("Cache setup finished");
        }

        private async Task SetupRepo()
        {
            try
            {
                var ciRef = Environment.GetEnvironmentVariable("CI_REF");
                var ciCommit = Environment.GetEnvironmentVariable("CI_COMMIT_HASH");
                var ciOrigin = Environment.GetEnvironmentVariable("CI_ORIGIN");

                QueueSendBasicMessage($"Checking out the needed ref: {ciRef} and commit: {ciCommit}");

                await GitRunHelpers.EnsureRepoIsCloned(ciOrigin, currentBuildRootFolder, CancellationToken.None);

                // Fetch the ref
                QueueSendBasicMessage($"Fetching the ref: {ciRef}");
                await GitRunHelpers.FetchRef(currentBuildRootFolder, ciRef, CancellationToken.None);

                await GitRunHelpers.Checkout(currentBuildRootFolder, ciCommit, CancellationToken.None, true);
                QueueSendBasicMessage($"Checked out commit {ciCommit}");

                // Clean out non-ignored files
                await GitRunHelpers.Clean(currentBuildRootFolder, CancellationToken.None);

                QueueSendBasicMessage("Cleaned non-ignored extra files");

                // TODO: implement handling of symlinked cache paths

                QueueSendBasicMessage("Repository checked out");
            }
            catch (Exception e)
            {
                EndSectionWithFailure($"Error cloning / checking out: {e}");
            }

            QueueSendBasicMessage("Cache setup finished");
        }

        private async Task SetupImages()
        {
        }

        private async Task RunBuild()
        {
        }

        private async Task RunPostBuild()
        {
        }

        private void QueueSendBasicMessage(string message)
        {
            QueueSendMessage(new RealTimeBuildMessage()
            {
                Type = BuildSectionMessageType.BuildOutput,
                Output = $"{message}\n"
            });
        }

        private void EndSectionWithFailure(string error)
        {
            QueueSendBasicMessage(error);

            QueueSendMessage(new RealTimeBuildMessage()
            {
                Type = BuildSectionMessageType.SectionEnd,
                WasSuccessful = false,
            });

            Failure = true;
        }

        private string HandleCacheTemplates(string cachePath)
        {
            return cachePath.Replace("{Branch}", localBranch);
        }

        private void AddMountConfiguration(List<string> arguments)
        {
            arguments.Add("--mount");
            arguments.Add(
                $"type=bind,source={currentBuildRootFolder},destination={currentBuildRootFolder},relabel=shared");
            arguments.Add("--mount");
            arguments.Add($"type=bind,source={sharedCacheFolder},destination={sharedCacheFolder},relabel=shared");
        }

        private async Task<CiBuildConfiguration> LoadCIBuildConfiguration(string folder)
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
                EndSectionWithFailure("Error reading or parsing build configuration file");
                return null;
            }
        }

        private List<string> BuildCommandsFromBuildConfig(CiBuildConfiguration configuration, string jobName,
            string folder)
        {
            if (!configuration.Jobs.TryGetValue(jobName, out CiJobConfiguration config))
            {
                QueueSendBasicMessage($"Config file is missing current job: #{jobName}");
                return null;
            }

            // Startup part
            var command = new List<string>
            {
                "echo 'Starting running build in container'",
                $"cd '{folder}' || {{ echo \"Couldn't switch to build folder\"; exit 1; }}",
                "echo 'Starting build commands'",
                $"echo '#{OutputSpecialCommandMarker} SectionEnd 0'",
            };

            // Build commands
            foreach (var step in config.Steps)
            {
                if (step.Run == null)
                    continue;

                var name = string.IsNullOrEmpty(step.Run.Name) ?
                    step.Run.Command.Substring(0, Math.Min(70, step.Run.Command.Length)) :
                    step.Run.Name;

                command.Add($"echo '#{OutputSpecialCommandMarker} SectionStart #{name}'");

                // Step is ran in subshell
                command.Add("(");
                command.Add("set -e");

                foreach (var line in step.Run.Command.Split('\n'))
                {
                    command.Add(line);
                }

                command.Add(")");
                command.Add($"echo \"#{OutputSpecialCommandMarker} SectionEnd $?\"");
            }

            return command;
        }

        private Task<bool> RunWithOutputStreaming(string executable, IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo(executable)
                { CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var taskCompletionSource = new TaskCompletionSource<bool>();

            var process = new Process()
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.Exited += (sender, args) =>
            {
                if (process.ExitCode != 0)
                {
                    Console.WriteLine("Failed to run: {0}", executable);
                }

                process.Dispose();
                taskCompletionSource.SetResult(process.ExitCode == 0);
            };

            process.OutputDataReceived += (sender, args) =>
            {
                QueueSendMessage(new RealTimeBuildMessage()
                {
                    Type = BuildSectionMessageType.BuildOutput,
                    Output = args.Data ?? "",
                });
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                QueueSendMessage(new RealTimeBuildMessage()
                {
                    Type = BuildSectionMessageType.BuildOutput,
                    Output = args.Data ?? "",
                });
            };

            if (!process.Start())
                throw new InvalidOperationException($"Could not start process: {process}");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return taskCompletionSource.Task;
        }

        private async Task<bool> RunWithInputAndOutput(List<string> inputLines, string executable,
            IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
                RedirectStandardInput = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var taskCompletionSource = new TaskCompletionSource<bool>();

            var process = new Process()
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.Exited += (sender, args) =>
            {
                if (process.ExitCode != 0)
                {
                    Console.WriteLine("Failed to run (with input): {0}", executable);
                }

                process.Dispose();
                taskCompletionSource.SetResult(process.ExitCode == 0);
            };

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data == null)
                    return;

                if (args.Data.StartsWith(OutputSpecialCommandMarker))
                {
                    // A special command
                    var parts = args.Data.Split(' ');

                    switch (parts[1])
                    {
                        case "SectionEnd":
                        {
                            var success = Convert.ToInt32(parts[2]) == 0;

                            QueueSendMessage(new RealTimeBuildMessage()
                            {
                                Type = BuildSectionMessageType.SectionEnd,
                                WasSuccessful = success,
                            });

                            if (!success)
                                Failure = true;

                            lastSectionClosed = true;
                            break;
                        }
                        case "SectionStart":
                        {
                            QueueSendMessage(new RealTimeBuildMessage()
                            {
                                Type = BuildSectionMessageType.SectionStart,
                                SectionName = string.Join(' ', parts.Skip(2)),
                            });

                            lastSectionClosed = false;
                            break;
                        }
                        default:
                        {
                            EndSectionWithFailure("Unknown special command received from build process");
                            break;
                        }
                    }
                }
                else
                {
                    // Normal output
                    QueueSendMessage(new RealTimeBuildMessage()
                    {
                        Type = BuildSectionMessageType.BuildOutput,
                        Output = args.Data ?? "",
                    });
                }
            };
            process.ErrorDataReceived += (sender, args) =>
            {
                QueueSendMessage(new RealTimeBuildMessage()
                {
                    Type = BuildSectionMessageType.BuildOutput,
                    Output = args.Data ?? "",
                });
            };

            if (!process.Start())
                throw new InvalidOperationException($"Could not start process: {process}");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            foreach (var line in inputLines)
            {
                if (process.HasExited)
                {
                    Console.WriteLine("Process exited before all input lines were written");
                    break;
                }

                await process.StandardInput.WriteLineAsync(line);
            }

            return await taskCompletionSource.Task;
        }
    }
}
