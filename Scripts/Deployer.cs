namespace Scripts;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using ScriptsBase.Models;
using ScriptsBase.Utilities;
using SharedBase.Utilities;

/// <summary>
///   Handles preparing files for deploy and creates database migrations for running
/// </summary>
public class Deployer
{
    private const string NET_VERSION = "net9.0";

    private const string BUILD_DATA_FOLDER_CONTAINER = "build";
    private const string BUILD_DATA_FOLDER_PLAIN = ".";
    private const string CONTAINER_NUGET_CACHE_HOST = "build/nuget";
    private const string CONTAINER_NUGET_CACHE_TARGET = "/root/.nuget";
    private const string BUILDER_CONTAINER_NAME = "thrive/devcenter-builder:latest";

    /// <summary>
    ///   Seems that for some reason subsequent container builds fail if this is not deleted
    /// </summary>
    private const string BUILD_FOLDER_TO_ALWAYS_DELETE = "build/Client";

    private const string MIGRATION_FILE = "migration.sql";
    private const string BLAZOR_BOOT_FILE = "blazor.boot.json";

    private static readonly IReadOnlyList<string> CIExecutorExtraResources =
    [
        "CIExecutor/bin/{0}/{1}/linux-x64/libMonoPosixHelper.so",
    ];

    private static readonly IReadOnlyList<string> PathsToCopyInContainer =
    [
        "AutomatedUITests",
        "CIExecutor",
        "Client",
        "Client.Tests",
        "RevolutionaryGamesCommon",
        "Scripts",
        "Server",
        "Server.Common",
        "Server.Tests",
        "Shared",
        "Shared.Tests",
        "Directory.Build.props",
        "global.json",
        "RevolutionaryWebApp.sln",
        "RevolutionaryWebApp.sln.DotSettings",
    ];

    private static readonly IReadOnlyList<string> PathsToDeleteInContainerAfterCopy =
    [
        "CIExecutor/bin",
        "CIExecutor/obj",
        "Client/bin",
        "Client/obj",
        "Server/bin",
        "Server/obj",
        "Server.Common/bin",
        "Server.Common/obj",
        "Shared/bin",
        "Shared/obj",
    ];

    private readonly DeployOptions options;

    public Deployer(DeployOptions options)
    {
        this.options = options;
    }

    public enum DeployMode
    {
        Staging,
        Production,
    }

    private string BuildDataFolder => options.DisableContainer ? BUILD_DATA_FOLDER_PLAIN : BUILD_DATA_FOLDER_CONTAINER;

    private string ClientBuiltWebroot => string.Format(BuildDataFolder + "/Client/bin/{0}/{1}/publish/wwwroot/",
        options.BuildMode, NET_VERSION);

    private string ServerBuiltBase =>
        string.Format(BuildDataFolder + "/Server/bin/{0}/{1}/publish/", options.BuildMode, NET_VERSION);

    private string CIExecutorBuiltFile =>
        string.Format(BuildDataFolder + "/CIExecutor/bin/{0}/{1}/linux-x64/publish/CIExecutor", options.BuildMode,
            NET_VERSION);

    public async Task<bool> Run(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.DeployTargetFolder))
        {
            ColourConsole.WriteErrorLine("Deployment target folder is a required parameter");
            return false;
        }

        ColourConsole.WriteInfoLine($"Starting deployment in mode {options.Mode}");
        cancellationToken.ThrowIfCancellationRequested();

        ColourConsole.WriteNormalLine("Creating database migration");

        // Ensure no other deploys will use the same migration file before we are done with it
        // This kind of doesn't really matter anymore with the new architecture... (deploy script runs the
        // migration now)
        await using var migrationFileMutex = new AsyncMutex("Global\\RevolutionaryWebAppDeployMigrationMutex");

        var mutexAcquireTime = new CancellationTokenSource();
        mutexAcquireTime.CancelAfter(TimeSpan.FromMinutes(15));

        try
        {
            await migrationFileMutex.AcquireAsync(mutexAcquireTime.Token);
        }
        catch (Exception e)
        {
            ColourConsole.WriteWarningLine("Cannot get global mutex lock: " + e);
            return false;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await CreateMigration(options.MigrationHashToVerify, cancellationToken))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ColourConsole.WriteNormalLine($"Building {options.BuildMode} files in a container");

            if (options.DisableContainer)
            {
                ColourConsole.WriteNormalLine("Performing a non-container build. Hopefully the environment is setup " +
                    "correctly!");
                if (!await PerformNonContainerBuild(cancellationToken))
                {
                    return false;
                }
            }
            else
            {
                if (!await PerformBuild(cancellationToken))
                {
                    return false;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            ColourConsole.WriteNormalLine("Performing deployment");
            ColourConsole.WriteInfoLine("Preparing files");
            if (!await CopyFiles(options.DeployTargetFolder, cancellationToken))
            {
                return false;
            }
        }
        finally
        {
            await migrationFileMutex.ReleaseAsync();
        }

        ColourConsole.WriteSuccessLine("Deploy files created successfully");
        ColourConsole.WriteNormalLine("Copy files to the target host and restart the services to finish deploy");

        return true;
    }

    private async Task<bool> CreateMigration(string? hashToVerify,
        CancellationToken cancellationToken)
    {
        // This is run before the build and doesn't make build artifacts,
        // so for now this is not build inside the build container

        var startInfo = new ProcessStartInfo("dotnet");
        startInfo.ArgumentList.Add("ef");
        startInfo.ArgumentList.Add("migrations");
        startInfo.ArgumentList.Add("script");
        startInfo.ArgumentList.Add("--idempotent");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(EFTool.SERVER_PROJECT_FILE);
        startInfo.ArgumentList.Add("--context");
        startInfo.ArgumentList.Add(EFTool.DB_CONTEXT);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(MIGRATION_FILE);

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to create migration");
            return false;
        }

        if (hashToVerify != null)
        {
            var hashRaw = await FileUtilities.CalculateSha3OfFile(MIGRATION_FILE, cancellationToken);
            var hash = FileUtilities.HashToHex(hashRaw);

            if (hash != hashToVerify || hash.Length < 5)
            {
                ColourConsole.WriteErrorLine("Migration hash has changed! Please manually verify everything is fine " +
                    $"and then update the deployment parameters. Expected hash: {hashToVerify}");
                ColourConsole.WriteNormalLine($"New hash: {hash}");
                return false;
            }
        }
        else
        {
            ColourConsole.WriteNormalLine("Not verifying hash of generated migration");
        }

        ColourConsole.WriteSuccessLine("Migration file written");
        ColourConsole.WriteNormalLine("Migration will need to be ran with the separate ansible deploy script");

        return true;
    }

    private async Task<bool> PerformBuild(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(BuildDataFolder);
        Directory.CreateDirectory(CONTAINER_NUGET_CACHE_HOST);

        if (Directory.Exists(BUILD_FOLDER_TO_ALWAYS_DELETE))
            Directory.Delete(BUILD_FOLDER_TO_ALWAYS_DELETE, true);

        var nugetCache = Path.GetFullPath(CONTAINER_NUGET_CACHE_HOST);

        var sourceDirectory = Path.GetFullPath(".");
        var buildTarget = Path.GetFullPath(BuildDataFolder);

        ColourConsole.WriteDebugLine("Storing build result in: " + buildTarget);

        var startInfo = new ProcessStartInfo("podman");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--rm");
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add("-e");

        // ReSharper disable once StringLiteralTypo
        startInfo.ArgumentList.Add("DOTNET_NOLOGO=true");

        startInfo.ArgumentList.Add($"--mount=type=bind,src={sourceDirectory},dst=/src,relabel=shared,ro=true");
        startInfo.ArgumentList.Add($"--mount=type=bind,src={buildTarget},dst=/build,relabel=shared");
        startInfo.ArgumentList.Add(
            $"--mount=type=bind,src={nugetCache},dst={CONTAINER_NUGET_CACHE_TARGET},relabel=shared");

        ColourConsole.WriteDebugLine($"Nuget cache inside container: {CONTAINER_NUGET_CACHE_TARGET}");

        startInfo.ArgumentList.Add(BUILDER_CONTAINER_NAME);

        startInfo.ArgumentList.Add("bash");
        startInfo.ArgumentList.Add("-c");

        var commandInContainer = new StringBuilder(500);
        commandInContainer.Append("set -e\n");

        foreach (var pathToCopy in PathsToCopyInContainer)
        {
            commandInContainer.Append($"cp -r '/src/{pathToCopy}' /build/\n");
        }

        foreach (var pathToDelete in PathsToDeleteInContainerAfterCopy)
        {
            commandInContainer.Append($"rm -rf /build/{pathToDelete}\n");
        }

        commandInContainer.Append("echo Nuget uses the following caches inside the container:\n");
        commandInContainer.Append("dotnet nuget locals all --list\n");

        commandInContainer.Append(
            $"cd /build && dotnet publish -c {options.BuildMode} && echo 'Success for build in container'");

        ColourConsole.WriteDebugLine($"Build command inside the container: \n{commandInContainer}");

        startInfo.ArgumentList.Add(commandInContainer.ToString());

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine(
                "Failed to build within container. Has the build container been built with the 'container' tool?");
            return false;
        }

        ColourConsole.WriteSuccessLine("Build within the build container succeeded");

        await VerifyBlazorBootFile(cancellationToken);

        ColourConsole.WriteSuccessLine("Build finished.");
        return true;
    }

    private async Task VerifyBlazorBootFile(CancellationToken cancellationToken)
    {
        ColourConsole.WriteNormalLine("Making sure blazor.boot.json has correct hashes");

        bool foundABootFile = false;

        foreach (var file in Directory.EnumerateFiles(ClientBuiltWebroot, BLAZOR_BOOT_FILE,
                     SearchOption.AllDirectories))
        {
            foundABootFile = true;

            ColourConsole.WriteNormalLine($"Checking boot file: {file}");
            await BlazorBootFileHandler.FixBootJSONHashes(file, cancellationToken);
        }

        if (!foundABootFile)
            ColourConsole.WriteWarningLine($"No {BLAZOR_BOOT_FILE} files found");
    }

    private async Task<bool> PerformNonContainerBuild(CancellationToken cancellationToken)
    {
        ColourConsole.WriteNormalLine("Writing published artifacts to current source tree");

        var startInfo = new ProcessStartInfo("dotnet");
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(options.BuildMode);

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to run publish command. Is there a build or environment error?");
            return false;
        }

        ColourConsole.WriteSuccessLine("Publish with dotnet succeeded");

        await VerifyBlazorBootFile(cancellationToken);

        ColourConsole.WriteSuccessLine("Build finished.");
        return true;
    }

    private async Task<bool> CopyFiles(string targetFolder, CancellationToken cancellationToken)
    {
        // Copy the CI executor to the webroot to be able to serve it
        ColourConsole.WriteNormalLine("Copying and compressing CI executor");
        var ciExecutorDestination = Path.Join(ClientBuiltWebroot, Path.GetFileName(CIExecutorBuiltFile));
        File.Copy(CIExecutorBuiltFile, ciExecutorDestination, true);
        await BlazorBootFileHandler.RegenerateCompressedFiles(ciExecutorDestination, cancellationToken);

        // And it also needs extra files...
        foreach (var extraResource in CIExecutorExtraResources)
        {
            var resource = Path.Join(BuildDataFolder, string.Format(extraResource, options.BuildMode, NET_VERSION));
            var destination = Path.Join(ClientBuiltWebroot, Path.GetFileName(resource));
            File.Copy(resource, destination, true);
            await BlazorBootFileHandler.RegenerateCompressedFiles(destination, cancellationToken);
        }

        var clientTarget = Path.Join(targetFolder, "www");
        var serverTarget = Path.Join(targetFolder, "server");

        try
        {
            Directory.CreateDirectory(clientTarget);
            Directory.CreateDirectory(serverTarget);
        }
        catch (Exception e)
        {
            ColourConsole.WriteErrorLine($"Failed to create install target folders: {e}");
            return false;
        }

        // Copy the migration file to the deployment target
        File.Copy(MIGRATION_FILE, Path.Combine(targetFolder, MIGRATION_FILE), true);

        var startInfo = new ProcessStartInfo("rsync");
        startInfo.ArgumentList.Add("-hr");
        startInfo.ArgumentList.Add(ClientBuiltWebroot);
        startInfo.ArgumentList.Add(clientTarget + "/");
        startInfo.ArgumentList.Add("--delete");

        ColourConsole.WriteNormalLine("Copying client files");

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken,
            !ColourConsole.DebugPrintingEnabled);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteNormalLine(result.FullOutput);
            ColourConsole.WriteErrorLine("Failed to copy files (see above for output)");
            return false;
        }

        startInfo = new ProcessStartInfo("rsync");
        startInfo.ArgumentList.Add("-hr");
        startInfo.ArgumentList.Add(ServerBuiltBase);
        startInfo.ArgumentList.Add(serverTarget + "/");
        startInfo.ArgumentList.Add("--delete");
        startInfo.ArgumentList.Add("--exclude");
        startInfo.ArgumentList.Add("wwwroot");

        // App settings is excluded as it has development environment secrets (in case it would get copied)
        startInfo.ArgumentList.Add("--exclude");
        startInfo.ArgumentList.Add("appsettings.Development.json");

        ColourConsole.WriteNormalLine("Copying server files");

        result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken,
            !ColourConsole.DebugPrintingEnabled);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to copy files (server)");
            return false;
        }

        ColourConsole.WriteSuccessLine("Build files synced to install folder");
        return true;
    }

    [Verb("deploy", HelpText = "Perform site deployment")]
    public class DeployOptions : ScriptOptionsBase
    {
        [Option('m', "mode", Default = DeployMode.Staging, MetaValue = "MODE",
            HelpText = "Selects deployment target from the default staging or production environment")]
        public DeployMode Mode { get; set; }

        [Option("install-target", Required = true, MetaValue = "FOLDER",
            HelpText =
                "Where to install the deployed files. These need to be then copied to the real server separately.")]
        public string DeployTargetFolder { get; set; } = string.Empty;

        // Turns out there really never was a need to run other kind of migrations
        /*[Option("migration-mode", Default = MigrationMode.Idempotent, MetaValue = "MODE",
            HelpText = "Sets the used migration execution mode")]
        public MigrationMode MigrationMode { get; set; }*/

        [Option("check-migration", Default = null, MetaValue = "HASH",
            HelpText = "If set, the generated migration script is verified to match this sha3 hash")]
        public string? MigrationHashToVerify { get; set; }

        [Option("dotnet-build-mode", Default = "Release", MetaValue = "MODE",
            HelpText = "Sets the target mode the dotnet build uses")]
        public string BuildMode { get; set; } = "Release";

        [Option("disable-container", Default = false,
            HelpText =
                "Specify to not use a container build (should be only done if already inside a container " +
                "with correct environment)")]
        public bool DisableContainer { get; set; }
    }
}
