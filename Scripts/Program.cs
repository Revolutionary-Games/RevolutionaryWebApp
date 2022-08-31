using System;
using System.Diagnostics;
using CommandLine;
using Scripts;
using ScriptsBase.Models;
using ScriptsBase.Utilities;
using SharedBase.Utilities;

public class Program
{
    public class CheckOptions : CheckOptionsBase
    {
    }

    [Verb("test", HelpText = "Run tests using 'dotnet' command")]
    public class TestOptions : ScriptOptionsBase
    {
    }

    [STAThread]
    public static int Main(string[] args)
    {
        RunFolderChecker.EnsureRightRunningFolder("ThriveDevCenter.sln");

        var result = CommandLineHelpers.CreateParser()
            .ParseArguments<CheckOptions, TestOptions, Deployer.DeployOptions, EFTool.EFOptions>(args)
            .MapResult(
                (CheckOptions opts) => RunChecks(opts),
                (TestOptions opts) => RunTests(opts),
                (Deployer.DeployOptions opts) => RunDeploy(opts),
                (EFTool.EFOptions opts) => RunEF(opts),
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
}
