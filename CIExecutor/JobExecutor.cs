namespace CIExecutor;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mono.Unix;
using RevolutionaryWebApp.Server.Common.Models;
using RevolutionaryWebApp.Server.Common.Services;
using RevolutionaryWebApp.Server.Common.Utilities;
using RevolutionaryWebApp.Shared;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Utilities;
using SharedBase.Utilities;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

/// <summary>
///   The main part of the job runner responsible for spawning the subprocesses to run the jobs (and to orchestrate
///   the cache and folder setup)
/// </summary>
public class JobExecutor : IJobExecutor, IDisposable
{
    // TODO: also need to fix the root cause of the git LFS pull not using cached assets

    private readonly ILogger logger;

    private readonly SemaphoreSlim semaphore = new(1, 1);

    private readonly SemaphoreSlim outputSemaphore = new(1, 1);

    private bool printBuildCommands = false;

    private string imageCacheFolder = "/unknown";
    private string ciImageFile = "unknown";
    private string ciImageName = "unknown";
    private string localBranch = "unknown";
    private string ciJobName = "unknown";

    private bool isSafe;

    private string cacheBaseFolder = "/unknown";
    private string sharedCacheFolder = "/unknown";
    private string jobCacheBaseFolder = "/unknown";

    private string ciRef = "unknown";
    private string defaultBranch = "unknown";

    private string? currentBuildRootFolder;

    private bool buildCommandsFailed;

    private string ciCommit = "unknown";
    private string ciPreviousCommit = "unknown";
    private string ciOrigin = "origin";

    private string? imageDownloadUrl;

    public JobExecutor(ILogger logger)
    {
        this.logger = logger;
        logger.LogInformation("Parsing variables from env");
    }

    public bool Failure { get; private set; }

    public bool Verbose { get; set; }

    public bool CreateStatusSection { get; set; }

    public async Task<bool> ExecuteJobAsync(CiJobCacheConfigurationEnriched cacheConfiguration, CIJobDTO jobDTO,
        IRunnerClientDataService dataService, IJobOutputForwarder jobOutput, IExecutorCache cache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cacheConfiguration.CIImageFileName) ||
            string.IsNullOrWhiteSpace(cacheConfiguration.CIImageName) ||
            string.IsNullOrWhiteSpace(cacheConfiguration.CIBranch) ||
            string.IsNullOrWhiteSpace(cacheConfiguration.CIJobName) ||
            string.IsNullOrWhiteSpace(cacheConfiguration.CIRef) ||
            string.IsNullOrWhiteSpace(cacheConfiguration.CIDefaultBranch) ||
            string.IsNullOrWhiteSpace(cacheConfiguration.CIOrigin) ||
            string.IsNullOrWhiteSpace(cacheConfiguration.CICommitHash) ||
            string.IsNullOrWhiteSpace(cacheConfiguration.CIEarlierCommit))
        {
            throw new Exception("CI cache data is not augmented with all required properties, cannot setup job data");
        }

        // Set up our state. These are fields so that we don't have to pass along an absolute ton of properties to each
        // method
        ciImageFile = cacheConfiguration.CIImageFileName;
        ciImageName = cacheConfiguration.CIImageName;
        localBranch = cacheConfiguration.CIBranch;
        ciJobName = cacheConfiguration.CIJobName;
        isSafe = cacheConfiguration.IsSafe == true;
        ciRef = cacheConfiguration.CIRef;
        defaultBranch = cacheConfiguration.CIDefaultBranch;
        ciCommit = cacheConfiguration.CICommitHash;
        ciOrigin = cacheConfiguration.CIOrigin;
        imageDownloadUrl = cacheConfiguration.CIImageDownloadUrl;
        ciPreviousCommit = cacheConfiguration.CIEarlierCommit;

        // Calculate job stats that we'll use
        imageCacheFolder = Path.Join(cache.BaseFolder, "images");
        cacheBaseFolder = isSafe ?
            Path.Join(cache.BaseFolder, "executor/safe") :
            Path.Join(cache.BaseFolder, "executor/unsafe");
        sharedCacheFolder = Path.Join(cacheBaseFolder, "shared");
        jobCacheBaseFolder = Path.Join(cacheBaseFolder, "named");
        ciImageFile = Path.Join(imageCacheFolder, cacheConfiguration.CIImageFileName);

        // Allow only one job at a time in case someone calls this multiple times to make sure we don't totally mess up
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var outputCutter = new TextToSectionCutAdapter(jobOutput);
            var result = await RunInternal(outputCutter, jobOutput, cacheConfiguration, jobDTO, cache,
                cancellationToken);
            return result;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to execute job due to exception");

            try
            {
                // For now, we don't expose the full exception to the potentially public build output log in case
                // it has any internal secrets
                var message = $"ERROR: runner encountered exception ({e.GetType().Name}): {e.Message}\n";
                if (!jobOutput.HasOpenSection)
                {
                    await jobOutput.OpenNewSection("Runner Exception");
                }

                await jobOutput.ForwardOutputToActiveSection(message);
                await jobOutput.CloseSection(false);
            }
            catch (Exception e2)
            {
                logger.LogWarning(e2, "Failed to send job output about exception");
            }

            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            semaphore.Dispose();
            outputSemaphore.Dispose();
        }
    }

    private static void AddSecretsConfiguration(List<CiSecretExecutorData> secrets, List<string> arguments)
    {
        foreach (var secret in secrets)
        {
            arguments.Add("-e");

            // Quoting cannot be used with podman (and also not with docker), so we can't do that here, hopefully
            // this is safe enough like this
            arguments.Add($"{secret.SecretName}={secret.SecretContent}");
        }
    }

    private async Task<bool> RunInternal(TextToSectionCutAdapter rawOutputReceiver, IJobOutputForwarder directOutput,
        CiJobCacheConfigurationEnriched cacheConfiguration, CIJobDTO jobDTO, IExecutorCache cache,
        CancellationToken cancellationToken)
    {
        Failure = false;
        buildCommandsFailed = false;

        // TODO: figure out can we make something better by reading the jobDTO and doing something with that?
        _ = jobDTO;

        if (directOutput.HasOpenSection)
        {
            await directOutput.ForwardOutputToActiveSection("ERROR: runner shouldn't have an open section...\n");
            await directOutput.CloseSection(false);
        }

        await directOutput.OpenNewSection("Environment Setup");

        logger.LogInformation("Going to check caches");
        if (!Failure)
            await SetupCaches(cacheConfiguration, directOutput, rawOutputReceiver, cancellationToken);

        if (!Failure)
            await SetupRepo(cacheConfiguration, directOutput, cancellationToken);

        if (!Failure)
            await SetupImages(directOutput, rawOutputReceiver, cache, cancellationToken);

        if (!Failure)
        {
            if (directOutput.HasOpenSection)
                await directOutput.CloseSection(true);

            await RunBuild(directOutput, rawOutputReceiver, cacheConfiguration, cancellationToken);
        }

        if (!Failure)
            await RunPostBuild(cancellationToken);

        // Close last section
        await rawOutputReceiver.Flush(cancellationToken);
        if (directOutput.HasOpenSection)
            await directOutput.CloseSection(!Failure);

        logger.LogInformation("Reached end of job, success: {Success}", !Failure);

        return !Failure && !buildCommandsFailed;
    }

    private async Task SetupCaches(CiJobCacheConfigurationEnriched cacheConfig, IJobOutputForwarder output,
        TextToSectionCutAdapter processOutput, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting cache setup");
        await output.ForwardOutputToActiveSection("Starting cache setup\n");

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
                await output.ForwardOutputToActiveSection(
                    $"Cache folder doesn't exist yet ({currentBuildRootFolder})\n");

                bool cacheCopied = false;

                foreach (var cachePath in cacheCopyFromFolders)
                {
                    logger.LogInformation($"Checking cache copy from folder: {cachePath}");
                    if (!Directory.Exists(cachePath))
                        continue;

                    if (!await RunWithOutputStreaming("cp", new List<string>
                        {
                            "-aT", cachePath, currentBuildRootFolder,
                        }, processOutput, cancellationToken))
                    {
                        throw new Exception("Failed to run cache copy command");
                    }

                    await output.ForwardOutputToActiveSection($"Initializing cache with copy from: {cachePath}\n");
                    cacheCopied = true;
                    break;
                }

                if (!cacheCopied)
                {
                    await output.ForwardOutputToActiveSection(
                        $"No existing cache found to copy from (last checked cache: " +
                        $"{cacheCopyFromFolders.LastOrDefault()})\n");
                }
            }
            else
            {
                await output.ForwardOutputToActiveSection(
                    $"Base build folder already exists at {currentBuildRootFolder}\n");
            }

            await output.ForwardOutputToActiveSection("Cache setup finished\n");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error setting up caches");
            await EndSectionWithFailure($"Error setting up caches: {e}", output);
        }
    }

    private async Task SetupRepo(CiJobCacheConfigurationEnriched cacheConfig, IJobOutputForwarder output,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting repo setup");

        try
        {
            await output.ForwardOutputToActiveSection($"Checking out the needed ref: {ciRef} and commit: {ciCommit}\n");

            var folder = currentBuildRootFolder ?? throw new Exception("build root folder not set");
            var parent = Path.GetDirectoryName(folder);
            if (parent != null)
                Directory.CreateDirectory(parent);

            // LFS is skipped here as this has caused a lot of extra bandwidth
            await GitRunHelpers.EnsureRepoIsCloned(ciOrigin, folder, true, cancellationToken);

            // Fetch the ref
            await output.ForwardOutputToActiveSection($"Fetching the ref: {ciRef}\n");
            await GitRunHelpers.FetchRef(folder, ciRef, cancellationToken);

            // Also fetch the default branch for comparing changes against it
            await GitRunHelpers.Fetch(folder, defaultBranch, ciOrigin, cancellationToken);

            await GitRunHelpers.Checkout(folder, ciCommit, true, cancellationToken, true);
            await output.ForwardOutputToActiveSection($"Checked out commit {ciCommit}\n");

            await GitRunHelpers.UpdateSubmodules(folder, true, true, cancellationToken);
            await output.ForwardOutputToActiveSection("Submodules are up to date\n");

            Dictionary<string, string> notAppliedCaches = new Dictionary<string, string>();

            // Early set up any LFS (git) related caches to save on bandwidth
            if (cacheConfig.Shared != null)
            {
                foreach (var tuple in cacheConfig.Shared)
                {
                    cancellationToken.ThrowIfCancellationRequested();

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

                    await output.ForwardOutputToActiveSection($"Early handling git cache {source} to {destination}\n");
                    await HandleSharedCache(fullSource, fullDestination, destination, output);
                }
            }

            // And only now pull the LFS
            var timer = new Stopwatch();
            timer.Start();
            await GitRunHelpers.LfsPull(folder, cancellationToken);
            await output.ForwardOutputToActiveSection($"LFS file pull completed in {timer.Elapsed}\n");

            // Clean out non-ignored files
            var deleted = await GitRunHelpers.Clean(folder, cancellationToken);

            await output.ForwardOutputToActiveSection($"Cleaned non-ignored extra files: {deleted}\n");

            // Handling of shared cache paths with symlinks
            foreach (var tuple in notAppliedCaches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullSource = tuple.Key;
                var fullDestination = tuple.Value;

                await HandleSharedCache(fullSource, fullDestination, Path.GetFileName(fullDestination), output);
            }

            await output.ForwardOutputToActiveSection("Repository checked out\n");
        }
        catch (Exception e)
        {
            await EndSectionWithFailure($"Error cloning / checking out: {e}", output);
        }
    }

    private async Task HandleSharedCache(string fullSource, string fullDestination, string destination,
        IJobOutputForwarder output)
    {
        // TODO: is a separate handling needed for when the fullSource is a single file and
        // not a directory?

        var isAlreadySymlink = Directory.Exists(fullSource) &&
            new UnixSymbolicLinkInfo(fullSource).IsSymbolicLink;

        if (Directory.Exists(fullSource) && !Directory.Exists(fullDestination) && !isAlreadySymlink)
        {
            await output.ForwardOutputToActiveSection($"Using existing folder to create shared cache {destination}\n");
            Directory.Move(fullSource, fullDestination);
        }

        if (!Directory.Exists(fullDestination))
        {
            await output.ForwardOutputToActiveSection($"Creating new shared cache {destination}\n");
            Directory.CreateDirectory(fullDestination);
        }

        if (isAlreadySymlink)
            return;

        if (Directory.Exists(fullSource))
        {
            await output.ForwardOutputToActiveSection(
                $"Deleting existing directory {Path.GetFileName(fullSource)} to link to shared cache {destination}\n");
            Directory.Delete(fullSource, true);
        }

        // Make sure the folder we are going to create the symbolic link in exists
        Directory.CreateDirectory(PathParser.GetParentPath(fullSource));

        await output.ForwardOutputToActiveSection($"Using shared cache {destination}\n");
        new UnixSymbolicLinkInfo(fullSource).CreateSymbolicLinkTo(fullDestination);
    }

    private async Task SetupImages(IJobOutputForwarder outputForwarder, TextToSectionCutAdapter processOutput,
        IExecutorCache mainCache, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting image setup");
        await outputForwarder.ForwardOutputToActiveSection($"Using build environment image: {ciImageName}\n");

        try
        {
            await outputForwarder.ForwardOutputToActiveSection($"Storing images in {imageCacheFolder}\n");

            // Check if podman already has the image, if it exists, we don't need to do anything further

            // TODO: switch this to use JSON format! `podman images --format json`

            var startInfo = new ProcessStartInfo("podman")
            {
                CreateNoWindow = true,
                ArgumentList = { "images" },
            };

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken);
            if (result.ExitCode != 0)
                throw new Exception($"Failed to check existing images from podman: {result.FullOutput}");

            try
            {
                await mainCache.NotifyUsedPodmanImage(ciImageName);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to notify main cache of podman image");
                await outputForwarder.ForwardOutputToActiveSection($"Cache failed to remember the used image: {e}\n");
            }

            var imageNameParts = ciImageName.Split(':');

            if (imageNameParts.Length != 2)
                throw new Exception("Image name part expected to have two parts separated by ':'");

            var existingImageLine = result.Output.Split('\n')
                .FirstOrDefault(l => l.Contains(imageNameParts[0]) && l.Contains(imageNameParts[1]));

            if (existingImageLine != null)
            {
                await outputForwarder.ForwardOutputToActiveSection(
                    $"Build environment image already exists: {existingImageLine}\n");
            }
            else
            {
                await LoadBuildImageToPodman(processOutput, cancellationToken);
                await processOutput.Flush(cancellationToken);
                await outputForwarder.ForwardOutputToActiveSection("Build environment image loaded\n");
            }

            await DeleteBuildImageDownload(outputForwarder);

            await outputForwarder.CloseSection(true);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error handling build image");
            await EndSectionWithFailure($"Error handling build image: {e}", outputForwarder);
        }
    }

    private async Task LoadBuildImageToPodman(TextToSectionCutAdapter output, CancellationToken cancellationToken)
    {
        if (!File.Exists(ciImageFile))
        {
            await DownloadBuildImage(output, cancellationToken);
        }

        if (!await RunWithOutputStreaming("podman", new List<string> { "load", "-i", ciImageFile }, output,
                cancellationToken))
        {
            // Delete a bad download
            try
            {
                File.Delete(ciImageFile);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete invalid image file");
            }

            throw new Exception("Failed to load the image file");
        }
    }

    private async Task DownloadBuildImage(TextToSectionCutAdapter output, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(PathParser.GetParentPath(ciImageFile));

        await output.OnProcessOutputLine("Build environment image doesn't exist locally, downloading...");

        if (string.IsNullOrWhiteSpace(imageDownloadUrl))
            throw new Exception("Missing image download URL");

        // TODO: swap this to using C# download instead of a separate process
        if (!await RunWithOutputStreaming("curl", new List<string>
            {
                "-LsS", imageDownloadUrl, "--output", ciImageFile,
            }, output, cancellationToken))
        {
            // Delete a partial download
            try
            {
                File.Delete(ciImageFile);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete failed image download");
            }

            throw new Exception("Failed to download image file");
        }
    }

    private async Task DeleteBuildImageDownload(IJobOutputForwarder output)
    {
        if (!File.Exists(ciImageFile))
            return;

        try
        {
            File.Delete(ciImageFile);
        }
        catch (Exception e)
        {
            logger.LogInformation(e, "Failed to delete already loaded build image file");
            await output.ForwardOutputToActiveSection(
                "Error: could not delete on-disk downloaded build image to save space\n");
        }
    }

    private async Task RunBuild(IJobOutputForwarder output, TextToSectionCutAdapter processOutput,
        CiJobCacheConfigurationEnriched buildInfo,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting build");
        try
        {
            await output.OpenNewSection("Build Start");

            await output.ForwardOutputToActiveSection($"Using build environment image: {ciImageName}\n");

            var folder = currentBuildRootFolder ?? throw new Exception("build root folder not set");

            CiBuildConfiguration buildConfig;
            try
            {
                buildConfig = await LoadCIBuildConfiguration(folder, cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogInformation(e, "Error reading build configuration file");
                await EndSectionWithFailure("Error reading or parsing build configuration file", output);
                return;
            }

            var command = await BuildCommandsFromBuildConfig(buildConfig, ciJobName, folder, output);

            if (command == null || command.Count < 1)
                throw new Exception("Failed to parse CI configuration to build list of build commands");

            if (printBuildCommands)
                PrintBuildCommands(command);

            List<CiSecretExecutorData>? secrets = buildInfo.CISecrets;
            if (secrets == null)
            {
                logger.LogWarning("Failed to load build secrets");
                throw new Exception("Failed to read build secrets");
            }

            var runArguments = new List<string>
            {
                // A little bit lower priority
                "-n", "4",
                "podman", "run", "--rm", "-i", "-e", $"CI_REF={ciRef}", "-e", $"CI_BRANCH={localBranch}",
                "-e", "TERM=xterm-256color", "-e", "CI=1",
                "-e", $"CI_COMMIT_HASH={ciCommit}",
                "-e", $"CI_EARLIER_COMMIT={ciPreviousCommit}",
                "-e", $"CI_DEFAULT_BRANCH={defaultBranch}",
            };

            AddMountConfiguration(runArguments);

            // We pass only the specific environment variables to the container
            AddSecretsConfiguration(secrets, runArguments);

            runArguments.Add(ciImageName);
            runArguments.Add("/bin/bash");

            cancellationToken.ThrowIfCancellationRequested();
            logger.LogInformation("Running podman build");
            var result = await RunWithInputAndOutput(command, "nice", runArguments, processOutput, cancellationToken);
            logger.LogInformation("Process finished: {Result}", result);

            if (!result)
                buildCommandsFailed = true;

            // Wait a little bit for last bits of output to reach us
            logger.LogInformation("Build process exited, waiting a little bit before finishing");
            await Task.Delay(1000, cancellationToken);

            try
            {
                await processOutput.Flush(cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to flush output");

                if (!output.HasOpenSection)
                    await output.OpenNewSection("Build Status Section");

                await output.ForwardOutputToActiveSection("Error, failed to flush output\n");
            }

            if (CreateStatusSection)
            {
                if (!output.HasOpenSection)
                    await output.OpenNewSection("Build Status Section");

                await output.ForwardOutputToActiveSection(result ?
                    "Build commands succeeded\n" :
                    "Build commands failed\n");

                await output.CloseSection(result);
            }
            else if (output.HasOpenSection)
            {
                await output.CloseSection(result);
            }
        }
        catch (Exception e)
        {
            await EndSectionWithFailure($"Error running build commands: {e}\n", output);
        }
    }

    private Task RunPostBuild(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting post-build");

        cancellationToken.ThrowIfCancellationRequested();

        // TODO: build artifacts
        return Task.CompletedTask;
    }

    private async Task EndSectionWithFailure(string error, IJobOutputForwarder output)
    {
        if (!output.HasOpenSection)
        {
            logger.LogWarning("No open section, so opening a section just to fail it then");
            await output.OpenNewSection("Section Error Report");
            await output.ForwardOutputToActiveSection("Opened a section just to report the following:\n");
        }

        logger.LogInformation("Failing current section with error: {Error}", error);
        await output.ForwardOutputToActiveSection(error + "\n");
        await output.CloseSection(false);
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

    private async Task<CiBuildConfiguration> LoadCIBuildConfiguration(string folder,
        CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(Path.Join(folder, AppInfo.CIConfigurationFile), Encoding.UTF8,
            cancellationToken);

        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var configuration = deserializer.Deserialize<CiBuildConfiguration>(text);

        if (configuration == null)
            throw new Exception("Deserialized is null");

        // We don't verify the model here, as it should have been verified by the job configuration creation
        // already

        return configuration;
    }

    private async Task<List<string>?> BuildCommandsFromBuildConfig(CiBuildConfiguration configuration, string jobName,
        string folder, IJobOutputForwarder information)
    {
        if (!configuration.Jobs.TryGetValue(jobName, out CiJobConfiguration? config))
        {
            await information.ForwardOutputToActiveSection($"Config file is missing current job: {jobName}\n");
            return null;
        }

        // Startup part
        var command = new List<string>
        {
            "echo 'Starting running build in container'",
        };

        if (config.Cache.System != null)
        {
            // Set up the system cache redirects
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
        command.Add($"echo '{TextToSectionCutAdapter.OutputSpecialCommandMarker} SectionEnd 0'");
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

            command.Add($"echo \"{TextToSectionCutAdapter.OutputSpecialCommandMarker} SectionStart " +
                $"{BashEscape.EscapeForBash(name)}\"");

            // Step is run in subshell
            command.Add("(");
            command.Add("set -e");

            foreach (var line in step.Run.Command.Split('\n'))
            {
                command.Add(line);
            }

            command.Add(")");

            command.Add("lastStatus=$?");
            command.Add("if [ ! $lastStatus -eq 0 ]; then");

            if (Verbose)
                command.Add("echo Running this section failed");

            command.Add("overallStatus=1");
            command.Add("fi");
            command.Add($"echo \"{TextToSectionCutAdapter.OutputSpecialCommandMarker} SectionEnd $lastStatus\"");
            command.Add("fi");
        }

        command.Add("exit 0");

        return command;
    }

    private void PrintBuildCommands(List<string> commands)
    {
        logger.LogInformation("Build commands:");
        foreach (var command in commands)
        {
            logger.LogInformation(command);
        }

        logger.LogInformation("end of build commands");
    }

    private async Task<bool> RunWithOutputStreaming(string executable, IEnumerable<string> arguments,
        TextToSectionCutAdapter output, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable) { CreateNoWindow = true };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var processAbort = new CancellationTokenSource(TimeSpan.FromHours(1));
        var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, processAbort.Token);

        // This variant combines error and normal output streams into one
        void SendNormalOutput(string line)
        {
            try
            {
                if (!outputSemaphore.Wait(TimeSpan.FromSeconds(500)))
                {
                    logger.LogError("Output processing is stuck, cancelling process");
                    if (!processAbort.IsCancellationRequested)
                        processAbort.Cancel();
                }

                try
                {
                    output.OnProcessOutputLine(line).Wait(TimeSpan.FromSeconds(600));
                }
                finally
                {
                    outputSemaphore.Release();
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in process simple output. Losing line: {Line}", line);
            }
        }

        ProcessRunHelpers.ProcessResult result;
        try
        {
            result = await ProcessRunHelpers.RunProcessWithOutputStreamingAsync(startInfo, combined.Token,
                SendNormalOutput, SendNormalOutput);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error running process: {Executable}", executable);
            return false;
        }

        if (result.ExitCode != 0)
        {
            if (result.ExitCode == ProcessRunHelpers.EXIT_STATUS_UNAVAILABLE)
                logger.LogWarning("Failed to read process result code");

            logger.LogInformation("Failed to run: {Executable}", executable);
            return false;
        }

        return true;
    }

    private async Task<bool> RunWithInputAndOutput(List<string> inputLines, string executable,
        IEnumerable<string> arguments, TextToSectionCutAdapter output, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable) { CreateNoWindow = true };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var processAbort = new CancellationTokenSource(TimeSpan.FromHours(2));
        var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, processAbort.Token);

        void SendNormalOutput(string line)
        {
            try
            {
                if (!outputSemaphore.Wait(TimeSpan.FromSeconds(500)))
                {
                    logger.LogError("Output processing is stuck, cancelling process");
                    if (!processAbort.IsCancellationRequested)
                        processAbort.Cancel();
                }

                try
                {
                    output.OnProcessOutputLine(line).Wait(TimeSpan.FromSeconds(600));
                }
                finally
                {
                    outputSemaphore.Release();
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in process normal output. Losing line: {Line}", line);
            }
        }

        void SendErrorOutput(string line)
        {
            try
            {
                if (!outputSemaphore.Wait(TimeSpan.FromSeconds(500)))
                {
                    logger.LogError("Output processing is stuck, cancelling process");
                    if (!processAbort.IsCancellationRequested)
                        processAbort.Cancel();
                }

                try
                {
                    output.OnProcessErrorLine(line).Wait(TimeSpan.FromSeconds(600));
                }
                finally
                {
                    outputSemaphore.Release();
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error in process error output. Losing line: {Line}", line);
            }
        }

        ProcessRunHelpers.ProcessResult result;
        try
        {
            result = await ProcessRunHelpers.RunProcessWithStdInAndOutputStreamingAsync(startInfo,
                combined.Token, inputLines, SendNormalOutput, SendErrorOutput);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error running process");
            return false;
        }

        if (!result.AllInputLinesWritten)
            logger.LogWarning("Process exited before all input lines were written");

        if (result.ErrorInInputLineClosing)
            logger.LogError("Failed to close input stream after writing input");

        if (result.ExitCode != 0)
        {
            if (result.ExitCode == ProcessRunHelpers.EXIT_STATUS_UNAVAILABLE)
                logger.LogWarning("Failed to read process result code");

            logger.LogInformation("Failed to run: {Executable}", executable);
            return false;
        }

        return true;
    }
}
