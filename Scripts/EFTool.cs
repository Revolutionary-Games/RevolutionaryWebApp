namespace Scripts;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using ScriptsBase.Models;
using ScriptsBase.Utilities;
using SharedBase.Utilities;

public class EFTool
{
    public const string SERVER_PROJECT_FILE = "Server/ThriveDevCenter.Server.csproj";
    public const string SERVER_FOLDER = "Server/";
    public const string DB_CONTEXT = "ApplicationDbContext";

    private readonly Regex aspNetVersionRegex = new(@"Include=""Microsoft\.AspNetCore\..+"" Version=""([0-9.]+)""");

    private readonly EFOptions options;

    public EFTool(EFOptions options)
    {
        this.options = options;

        if (!string.IsNullOrEmpty(options.Recreate))
        {
            options.Remove = true;
            options.Create = options.Recreate;
        }

        if (options.Redo is { Count: > 0 })
        {
            if (options.Redo.Count != 2)
                ConsoleHelpers.ExitWithError("Redo option must be given 2 values");

            options.Downgrade = options.Redo[0];
            options.Remove = true;
            options.Create = options.Redo[1];
            options.Migrate = true;
        }
    }

    public async Task<bool> Run(CancellationToken cancellationToken)
    {
        bool didSomething = false;

        if (options.Install)
        {
            ColourConsole.WriteInfoLine("Installing dotnet-ef tool");
            didSomething = true;

            var startInfo = new ProcessStartInfo("dotnet");
            startInfo.ArgumentList.Add("tool");
            startInfo.ArgumentList.Add("install");
            startInfo.ArgumentList.Add("--global");
            startInfo.ArgumentList.Add("dotnet-ef");
            startInfo.ArgumentList.Add("--version");
            startInfo.ArgumentList.Add(DetectVersion());

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

            if (result.ExitCode != 0)
            {
                ColourConsole.WriteErrorLine("Failed to install");
                return false;
            }
        }
        else if (options.Update)
        {
            ColourConsole.WriteInfoLine("Updating dotnet-ef tool");
            didSomething = true;

            var startInfo = new ProcessStartInfo("dotnet");
            startInfo.ArgumentList.Add("tool");
            startInfo.ArgumentList.Add("update");
            startInfo.ArgumentList.Add("--global");
            startInfo.ArgumentList.Add("dotnet-ef");
            startInfo.ArgumentList.Add("--version");
            startInfo.ArgumentList.Add(DetectVersion());

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

            if (result.ExitCode != 0)
            {
                ColourConsole.WriteErrorLine("Failed to update dotnet-ef");
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrEmpty(options.Downgrade))
        {
            ColourConsole.WriteInfoLine($"Migrating local database back down to version {options.Downgrade}");
            didSomething = true;

            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = SERVER_FOLDER,
            };
            startInfo.ArgumentList.Add("ef");
            startInfo.ArgumentList.Add("database");
            startInfo.ArgumentList.Add("update");
            startInfo.ArgumentList.Add(options.Downgrade);
            startInfo.ArgumentList.Add("--context");
            startInfo.ArgumentList.Add(DB_CONTEXT);

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

            if (result.ExitCode != 0)
            {
                ColourConsole.WriteErrorLine("Failed to migrate local database to target migration");
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (options.Remove)
        {
            ColourConsole.WriteInfoLine("Removing latest migration");
            didSomething = true;

            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = SERVER_FOLDER,
            };
            startInfo.ArgumentList.Add("ef");
            startInfo.ArgumentList.Add("migrations");
            startInfo.ArgumentList.Add("remove");
            startInfo.ArgumentList.Add("--context");
            startInfo.ArgumentList.Add(DB_CONTEXT);

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

            if (result.ExitCode != 0)
            {
                ColourConsole.WriteErrorLine("Failed to remove migration");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(options.Create))
        {
            ColourConsole.WriteInfoLine($"Creating migration {options.Create}");
            didSomething = true;

            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = SERVER_FOLDER,
            };
            startInfo.ArgumentList.Add("ef");
            startInfo.ArgumentList.Add("migrations");
            startInfo.ArgumentList.Add("add");
            startInfo.ArgumentList.Add(options.Create);
            startInfo.ArgumentList.Add("--context");
            startInfo.ArgumentList.Add(DB_CONTEXT);

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

            if (result.ExitCode != 0)
            {
                ColourConsole.WriteErrorLine("Failed to create migration");
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (options.Migrate)
        {
            ColourConsole.WriteInfoLine("Migrating local database");
            didSomething = true;

            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = SERVER_FOLDER,
            };
            startInfo.ArgumentList.Add("ef");
            startInfo.ArgumentList.Add("database");
            startInfo.ArgumentList.Add("update");
            startInfo.ArgumentList.Add("--context");
            startInfo.ArgumentList.Add(DB_CONTEXT);

            var result = await ProcessRunHelpers.RunProcessAsync(startInfo, cancellationToken, false);

            if (result.ExitCode != 0)
            {
                ColourConsole.WriteErrorLine("Failed to update local database");
                return false;
            }
        }

        if (!didSomething)
        {
            ColourConsole.WriteErrorLine("No operations to perform specified");
            return false;
        }

        ColourConsole.WriteSuccessLine("Finished operations");
        return true;
    }

    private string DetectVersion()
    {
        foreach (var line in File.ReadLines(SERVER_PROJECT_FILE))
        {
            var match = aspNetVersionRegex.Match(line);

            if (match.Success)
            {
                ColourConsole.WriteDebugLine($"Detected aspnet version in {SERVER_PROJECT_FILE} on line: {line}");

                var wanted = match.Groups[1].Value;
                ColourConsole.WriteNormalLine($"Detected wanted aspnet version: {wanted}");
                return wanted;
            }
        }

        throw new Exception("Could not detect wanted aspnet version");
    }

    [Verb("ef", HelpText = "Perform EntityFramework (ef) helper operations")]
    public class EFOptions : ScriptOptionsBase
    {
        [Option('i', "install", Default = false,
            HelpText = "Install the tool (if already installed use \"-u\" instead)")]
        public bool Install { get; set; }

        [Option('u', "update", Default = false, HelpText = "Update the tool")]
        public bool Update { get; set; }

        [Option('c', "create", Default = null, MetaValue = "MIGRATION_NAME", HelpText = "Create a new migration")]
        public string? Create { get; set; }

        [Option('m', "migrate", Default = false, HelpText = "Run database migrations against the local database")]
        public bool Migrate { get; set; }

        [Option('r', "remove", Default = false,
            HelpText = "Remove the latest migration (db needs to be down migrated first)")]
        public bool Remove { get; set; }

        [Option('d', "downgrade", Default = null, MetaValue = "MIGRATION",
            HelpText = "Downgrades the local database to specified migration level (0 to clear entirely)")]
        public string? Downgrade { get; set; }

        [Option('b', "recreate", Default = null, MetaValue = "MIGRATION",
            HelpText = "Recreates latest migration. If DB is already migrated use \"--redo\" instead")]
        public string? Recreate { get; set; }

        [Option("redo", Default = null, MetaValue = "DOWNGRADE_TO LATEST_MIGRATION", Separator = ',',
            HelpText = "Down migrates the db and recreates the latest migration")]
        public IList<string>? Redo { get; set; }
    }
}
