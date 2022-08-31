using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CommandLine;
using Scripts;
using ScriptsBase.Models;
using ScriptsBase.Utilities;
using SharedBase.Utilities;

public class Program
{
    private static readonly IEnumerable<string> DefaultFoldersToClean = new List<string>
    {
        "AutomatedUITests", "CIExecutor", "Client", "Client.Tests", "Server", "Server.Common", "Server.Tests", "Shared",
        "Shared.Tests", "RevolutionaryGamesCommon/SharedBase",
    };

    public class CheckOptions : CheckOptionsBase
    {
    }

    [Verb("test", HelpText = "Run tests using 'dotnet' command")]
    public class TestOptions : ScriptOptionsBase
    {
    }

    [Verb("clean", HelpText = "Clean binaries (package upgrades can break deploy and this fixes that)")]
    public class CleanOptions : ScriptOptionsBase
    {
    }

    public class ChangesOptions : ChangesOptionsBase
    {
        [Option('b', "branch", Required = false, Default = "master", HelpText = "The git remote branch name")]
        public override string RemoteBranch { get; set; } = "master";
    }

    [STAThread]
    public static int Main(string[] args)
    {
        RunFolderChecker.EnsureRightRunningFolder("ThriveDevCenter.sln");

        var result = CommandLineHelpers.CreateParser()
            .ParseArguments<CheckOptions, TestOptions, Deployer.DeployOptions, EFTool.EFOptions, CleanOptions,
                ChangesOptions>(args)
            .MapResult(
                (CheckOptions opts) => RunChecks(opts),
                (TestOptions opts) => RunTests(opts),
                (Deployer.DeployOptions opts) => RunDeploy(opts),
                (EFTool.EFOptions opts) => RunEF(opts),
                (CleanOptions opts) => RunClean(opts),
                (ChangesOptions opts) => RunChangesFinding(opts),
                CommandLineHelpers.PrintCommandLineErrors);

        ConsoleHelpers.CleanConsoleStateForExit();

        return result;
    }

    private static int RunChecks(CheckOptions opts)
    {
        CommandLineHelpers.HandleDefaultOptions(opts);

        ColourConsole.WriteDebugLine("Running in check mode");
        ColourConsole.WriteDebugLine($"Manually specified checks: {string.Join(' ', opts.Checks)}");

        var checker = new CodeChecks(opts);

        return checker.Run().Result;
    }

    private static int RunTests(TestOptions opts)
    {
        CommandLineHelpers.HandleDefaultOptions(opts);

        ColourConsole.WriteDebugLine("Running dotnet tests");

        var tokenSource = ConsoleHelpers.CreateSimpleConsoleCancellationSource();

        return ProcessRunHelpers.RunProcessAsync(new ProcessStartInfo("dotnet", "test"), tokenSource.Token, false)
            .Result.ExitCode;
    }

    private static int RunDeploy(Deployer.DeployOptions opts)
    {
        CommandLineHelpers.HandleDefaultOptions(opts);

        ColourConsole.WriteDebugLine("Running deployment tool");

        var tokenSource = ConsoleHelpers.CreateSimpleConsoleCancellationSource();

        var deployer = new Deployer(opts);

        if (deployer.Run(tokenSource.Token).Result)
            return 0;

        return 2;
    }

    private static int RunEF(EFTool.EFOptions opts)
    {
        CommandLineHelpers.HandleDefaultOptions(opts);

        ColourConsole.WriteDebugLine("Running EF helper tool");

        var tokenSource = ConsoleHelpers.CreateSimpleConsoleCancellationSource();

        var checker = new EFTool(opts);

        if (checker.Run(tokenSource.Token).Result)
            return 0;

        return 2;
    }

    private static int RunClean(CleanOptions opts)
    {
        _ = opts;

        ColourConsole.WriteDebugLine("Running cleaning tool");

        foreach (var folder in DefaultFoldersToClean)
        {
            if (!Directory.Exists(folder))
            {
                ColourConsole.WriteErrorLine($"Folder to clean in doesn't exist: {folder}");
                continue;
            }

            CleanIfExists(Path.Join(folder, "bin"));
            CleanIfExists(Path.Join(folder, "obj"));
        }

        return 0;
    }

    private static void CleanIfExists(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        ColourConsole.WriteNormalLine($"Deleting: {folder}");

        try
        {
            Directory.Delete(folder, true);
        }
        catch (Exception e)
        {
            ColourConsole.WriteErrorLine($"Failed to delete a folder ({folder}): {e}");
        }
    }

    private static int RunChangesFinding(ChangesOptions opts)
    {
        ColourConsole.WriteDebugLine("Running changes finding tool");

        return OnlyChangedFileDetector.BuildListOfChangedFiles(opts).Result ? 0 : 1;
    }
}
