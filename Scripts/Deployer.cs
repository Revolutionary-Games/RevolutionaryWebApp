namespace Scripts;

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

public class Deployer
{
    private const string NET_VERSION = "net8.0";

    private const string BUILD_DATA_FOLDER = "build";
    private const string CONTAINER_NUGET_CACHE_HOST = "build/nuget";
    private const string CONTAINER_NUGET_CACHE_TARGET = "/root/.nuget";
    private const string BUILDER_CONTAINER_NAME = "thrive/devcenter-builder:latest";

    /// <summary>
    ///   Seems that for some reason subsequent container builds fail if this is not deleted
    /// </summary>
    private const string BUILD_FOLDER_TO_ALWAYS_DELETE = "build/Client";

    private const string MIGRATION_FILE = "migration.sql";
    private const string BLAZOR_BOOT_FILE = "blazor.boot.json";

    private const string CLIENT_BUILT_WEBROOT = BUILD_DATA_FOLDER + "/Client/bin/{0}/{1}/publish/wwwroot/";
    private const string SERVER_BUILT_BASE = BUILD_DATA_FOLDER + "/Server/bin/{0}/{1}/publish/";

    private const string CI_EXECUTOR_BUILT_FILE =
        BUILD_DATA_FOLDER + "/CIExecutor/bin/{0}/{1}/linux-x64/publish/CIExecutor";

    private const string DEFAULT_TARGET_HOST_WWW_ROOT = "/var/www/thrivedevcenter/{0}";
    private const string DEFAULT_TARGET_HOST_APP_ROOT = "/opt/thrivedevcenter/{0}";

    private const string DEFAULT_PRODUCTION_HOST = "dev.revolutionarygamesstudio.com";
    private const string DEFAULT_PRODUCTION_DATABASE = "thrivedevcenter";
    private const string DEFAULT_PRODUCTION_SERVICE = "thrivedevcenter";

    private const string DEFAULT_STAGING_HOST = "staging.dev.revolutionarygamesstudio.com";
    private const string DEFAULT_STAGING_DATABASE = "thrivedevcenter_staging";
    private const string DEFAULT_STAGING_SERVICE = "thrivedevcenter-staging";

    private const string SSH_USERNAME = "root";

    private static readonly IReadOnlyList<string> CIExecutorExtraResources = new[]
    {
        "CIExecutor/bin/{0}/{1}/linux-x64/libMonoPosixHelper.so",
    };

    private static readonly IReadOnlyList<string> PathsToCopyInContainer = new[]
    {
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
        "ThriveDevCenter.sln",
        "ThriveDevCenter.sln.DotSettings",
    };

    private static readonly IReadOnlyList<string> PathsToDeleteInContainerAfterCopy = new[]
    {
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
    };

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

    public enum MigrationMode
    {
        // TODO: add running only specific migrations mode
        Idempotent,
    }

    private string ClientBuiltWebroot => string.Format(CLIENT_BUILT_WEBROOT, options.BuildMode, NET_VERSION);

    private string ServerBuiltBase => string.Format(SERVER_BUILT_BASE, options.BuildMode, NET_VERSION);

    private string CIExecutorBuiltFile => string.Format(CI_EXECUTOR_BUILT_FILE, options.BuildMode, NET_VERSION);

    private string TargetWWWRoot => string.Format(GetTargetWWWRoot(), options.Mode.ToString().ToLowerInvariant());

    private string TargetAppRoot => string.Format(GetTargetAppRoot(), options.Mode.ToString().ToLowerInvariant());

    public async Task<bool> Run(CancellationToken cancellationToken)
    {
        var targetHost = options.OverrideDeployHost;

        if (string.IsNullOrEmpty(targetHost))
        {
            targetHost = options.Mode == DeployMode.Production ? DEFAULT_PRODUCTION_HOST : DEFAULT_STAGING_HOST;
        }

        ColourConsole.WriteInfoLine($"Starting deployment to {targetHost}");
        cancellationToken.ThrowIfCancellationRequested();

        // TODO: send site notification about the impending downtime

        if (options.Migrate == true)
        {
            ColourConsole.WriteNormalLine("Performing migration");
            if (!await PerformMigration(targetHost, cancellationToken))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        ColourConsole.WriteNormalLine($"Building {options.BuildMode} files in a container");
        if (!await PerformBuild(cancellationToken))
        {
            return false;
        }

        if (options.Deploy == true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ColourConsole.WriteNormalLine("Performing deployment");
            ColourConsole.WriteInfoLine("Sending files");
            if (!await SendFiles(targetHost, cancellationToken))
            {
                return false;
            }

            ColourConsole.WriteInfoLine("Restarting services on the server");
            if (!await RestartTargetHostServices(targetHost, cancellationToken))
            {
                return false;
            }

            ColourConsole.WriteSuccessLine("Deployed successfully");
        }
        else
        {
            ColourConsole.WriteNormalLine("Skipping deploy");
        }

        return true;
    }

    private async Task<bool> PerformMigration(string targetHost, CancellationToken cancellationToken)
    {
        // This is ran before the build and is optional, so for now this is not build inside the build container

        var startInfo = new ProcessStartInfo("dotnet");
        startInfo.ArgumentList.Add("ef");
        startInfo.ArgumentList.Add("migrations");
        startInfo.ArgumentList.Add("script");
        startInfo.ArgumentList.Add($"--{options.MigrationMode.ToString().ToLowerInvariant()}");
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

        ColourConsole.WriteWarningLine(
            "Please check 'migration.sql' for accuracy, then press enter to continue (or CTRL-C to cancel)");
        if (!await ConsoleHelpers.WaitForInputToContinue(cancellationToken))
        {
            ColourConsole.WriteInfoLine("Migration canceled, quitting");
            return false;
        }

        ColourConsole.WriteNormalLine("Sending migration to server");

        startInfo = new ProcessStartInfo("scp");
        startInfo.ArgumentList.Add(MIGRATION_FILE);
        startInfo.ArgumentList.Add($"{SSH_USERNAME}@{targetHost}:{MIGRATION_FILE}");

        result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to copy migration to server");
            return false;
        }

        var database = GetDatabaseName();
        ColourConsole.WriteNormalLine($"Running migration on database {database}...");

        var sshCommandStringBuilder = new StringBuilder();

        startInfo = new ProcessStartInfo("ssh");
        startInfo.ArgumentList.Add($"{SSH_USERNAME}@{targetHost}");

        sshCommandStringBuilder.Append("su - postgres -c ");
        sshCommandStringBuilder.Append("\"");
        sshCommandStringBuilder.Append("psql -d ");
        sshCommandStringBuilder.Append(database);
        sshCommandStringBuilder.Append("\"");
        sshCommandStringBuilder.Append($" < {MIGRATION_FILE}");

        startInfo.ArgumentList.Add(sshCommandStringBuilder.ToString());

        result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken,
            !ColourConsole.DebugPrintingEnabled);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteNormalLine(result.FullOutput);
            ColourConsole.WriteErrorLine("Failed to run migration on the server");
            return false;
        }

        ColourConsole.WriteNormalLine("Migration succeeded");
        ColourConsole.WriteInfoLine("Trying to fudge grants...");

        sshCommandStringBuilder.Clear();
        sshCommandStringBuilder.Append("su - postgres -c ");

        sshCommandStringBuilder.Append('"');
        sshCommandStringBuilder.Append("psql -d ");
        sshCommandStringBuilder.Append(database);
        sshCommandStringBuilder.Append(" -c ");
        sshCommandStringBuilder.Append('\'');
        sshCommandStringBuilder.Append(GetGrantFromType("TABLES", database));
        sshCommandStringBuilder.Append(GetGrantFromType("SEQUENCES", database));
        sshCommandStringBuilder.Append('\'');
        sshCommandStringBuilder.Append('"');

        startInfo.ArgumentList.Add(sshCommandStringBuilder.ToString());

        result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken,
            !ColourConsole.DebugPrintingEnabled);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteNormalLine(result.FullOutput);
            ColourConsole.WriteErrorLine("Failed to fudge grants");
            return false;
        }

        ColourConsole.WriteSuccessLine("Migration complete");

        return true;
    }

    private async Task<bool> PerformBuild(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(BUILD_DATA_FOLDER);
        Directory.CreateDirectory(CONTAINER_NUGET_CACHE_HOST);

        if (Directory.Exists(BUILD_FOLDER_TO_ALWAYS_DELETE))
            Directory.Delete(BUILD_FOLDER_TO_ALWAYS_DELETE, true);

        var nugetCache = Path.GetFullPath(CONTAINER_NUGET_CACHE_HOST);

        var sourceDirectory = Path.GetFullPath(".");
        var buildTarget = Path.GetFullPath(BUILD_DATA_FOLDER);

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

        ColourConsole.WriteSuccessLine("Build finished.");
        return true;
    }

    private async Task<bool> SendFiles(string targetHost, CancellationToken cancellationToken)
    {
        // Copy the CI executor to the webroot to be able to serve it
        ColourConsole.WriteNormalLine("Copying and compressing CI executor");
        var ciExecutorDestination = Path.Join(ClientBuiltWebroot, Path.GetFileName(CIExecutorBuiltFile));
        File.Copy(CIExecutorBuiltFile, ciExecutorDestination, true);
        await BlazorBootFileHandler.RegenerateCompressedFiles(ciExecutorDestination, cancellationToken);

        // And it also needs extra files...
        foreach (var extraResource in CIExecutorExtraResources)
        {
            var resource = Path.Join(BUILD_DATA_FOLDER, string.Format(extraResource, options.BuildMode, NET_VERSION));
            var destination = Path.Join(ClientBuiltWebroot, Path.GetFileName(resource));
            File.Copy(resource, destination, true);
            await BlazorBootFileHandler.RegenerateCompressedFiles(destination, cancellationToken);
        }

        var startInfo = new ProcessStartInfo("rsync");
        startInfo.ArgumentList.Add("-hr");
        startInfo.ArgumentList.Add(ClientBuiltWebroot);
        startInfo.ArgumentList.Add($"{SSH_USERNAME}@{targetHost}:{TargetWWWRoot}");
        startInfo.ArgumentList.Add("--delete");

        ColourConsole.WriteNormalLine("Sending client files");

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken,
            !ColourConsole.DebugPrintingEnabled);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to send files");
            return false;
        }

        startInfo = new ProcessStartInfo("rsync");
        startInfo.ArgumentList.Add("-hr");
        startInfo.ArgumentList.Add(ServerBuiltBase);
        startInfo.ArgumentList.Add($"{SSH_USERNAME}@{targetHost}:{TargetAppRoot}");
        startInfo.ArgumentList.Add("--delete");
        startInfo.ArgumentList.Add("--exclude");
        startInfo.ArgumentList.Add("wwwroot");

        // App settings is excluded as it has development environment secrets
        startInfo.ArgumentList.Add("--exclude");
        startInfo.ArgumentList.Add("appsettings.Development.json");

        ColourConsole.WriteNormalLine("Sending server files");

        result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken,
            !ColourConsole.DebugPrintingEnabled);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to send files (server)");
            return false;
        }

        ColourConsole.WriteSuccessLine("Build Files synced to server");
        return true;
    }

    private async Task<bool> RestartTargetHostServices(string targetHost, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("ssh");
        startInfo.ArgumentList.Add($"{SSH_USERNAME}@{targetHost}");
        startInfo.ArgumentList.Add("systemctl");
        startInfo.ArgumentList.Add("restart");
        startInfo.ArgumentList.Add(GetServiceName());

        var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken,
            !ColourConsole.DebugPrintingEnabled);

        if (result.ExitCode != 0)
        {
            ColourConsole.WriteErrorLine("Failed to restart services");
            return false;
        }

        return true;
    }

    private string GetDatabaseName()
    {
        if (!string.IsNullOrEmpty(options.OverrideDeployDatabase))
            return options.OverrideDeployDatabase;

        if (options.Mode == DeployMode.Production)
            return DEFAULT_PRODUCTION_DATABASE;

        return DEFAULT_STAGING_DATABASE;
    }

    private string GetServiceName()
    {
        if (!string.IsNullOrEmpty(options.OverrideDeployServiceName))
            return options.OverrideDeployServiceName;

        if (options.Mode == DeployMode.Production)
            return DEFAULT_PRODUCTION_SERVICE;

        return DEFAULT_STAGING_SERVICE;
    }

    private string GetTargetWWWRoot()
    {
        if (!string.IsNullOrEmpty(options.OverrideDeployWWWRoot))
            return options.OverrideDeployWWWRoot;

        return DEFAULT_TARGET_HOST_WWW_ROOT;
    }

    private string GetTargetAppRoot()
    {
        if (!string.IsNullOrEmpty(options.OverrideDeployAppRoot))
            return options.OverrideDeployAppRoot;

        return DEFAULT_TARGET_HOST_APP_ROOT;
    }

    private string GetGrantFromType(string type, string databaseName)
    {
        if (type == "SEQUENCES")
        {
            return $"GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO {databaseName};";
        }

        return $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL {type} IN SCHEMA public TO {databaseName};";
    }

    [Verb("deploy", HelpText = "Perform site deployment")]
    public class DeployOptions : ScriptOptionsBase
    {
        [Option('m', "mode", Default = DeployMode.Staging, MetaValue = "MODE",
            HelpText = "Selects deployment target from the default staging or production environment")]
        public DeployMode Mode { get; set; }

        [Option("migrate", Default = true,
            HelpText = "When enabled the deploy target runs migrations before deployment")]
        public bool? Migrate { get; set; }

        [Option("migration-mode", Default = MigrationMode.Idempotent, MetaValue = "MODE",
            HelpText = "Sets the used migration execution mode")]
        public MigrationMode MigrationMode { get; set; }

        [Option("deploy", Default = true,
            HelpText = "Controls whether final deployment is actually performed")]
        public bool? Deploy { get; set; }

        [Option("dotnet-build-mode", Default = "Release", MetaValue = "MODE",
            HelpText = "Sets the target mode the dotnet build uses")]
        public string BuildMode { get; set; } = "Release";

        [Option("override-deploy-www-folder", Default = null, MetaValue = "FOLDER",
            HelpText = "Override the folder on the target server where deployment is performed")]
        public string? OverrideDeployWWWRoot { get; set; }

        [Option("override-deploy-app-folder", Default = null, MetaValue = "FOLDER",
            HelpText = "Override the folder on the target server where deployment is performed")]
        public string? OverrideDeployAppRoot { get; set; }

        [Option("override-deploy-host", Default = null, MetaValue = "HOSTNAME",
            HelpText = "Override the host the deployment targets")]
        public string? OverrideDeployHost { get; set; }

        [Option("override-deploy-database", Default = null, MetaValue = "DATABASE",
            HelpText = "Override the database that is migrated on the target host")]
        public string? OverrideDeployDatabase { get; set; }

        [Option("override-deploy-service-name", Default = null, MetaValue = "NAME",
            HelpText = "Override the service name that is restarted on the target after deployment")]
        public string? OverrideDeployServiceName { get; set; }
    }
}
