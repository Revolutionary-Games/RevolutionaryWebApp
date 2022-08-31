using System;
using System.Diagnostics;
using System.Threading;
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

    [Verb("deploy", HelpText = "Perform site deployment")]
    public class DeployOptions : ScriptOptionsBase
    {
    }

    [Verb("ef", HelpText = "Perform EntityFramework (ef) helper operations")]
    public class EFOptions : ScriptOptionsBase
    {
    }

    [STAThread]
    public static int Main(string[] args)
    {
        RunFolderChecker.EnsureRightRunningFolder("ThriveDevCenter.sln");

        var result = CommandLineHelpers.CreateParser()
            .ParseArguments<CheckOptions, TestOptions, DeployOptions, EFOptions>(args)
            .MapResult(
                (CheckOptions opts) => RunChecks(opts),
                (TestOptions opts) => RunTests(opts),
                (DeployOptions opts) => RunDeploy(opts),
                (EFOptions opts) => RunEF(opts),
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
    
    private static int RunDeploy(DeployOptions opts)
    {
        CommandLineHelpers.HandleDefaultOptions(opts);

        ColourConsole.WriteInfo("Starting deployment");

        var tokenSource = ConsoleHelpers.CreateSimpleConsoleCancellationSource();

        throw new NotImplementedException();
    }
    
    private static int RunEF(EFOptions opts)
    {
        CommandLineHelpers.HandleDefaultOptions(opts);

        ColourConsole.WriteDebugLine("Running EF helper tool");

        var tokenSource = ConsoleHelpers.CreateSimpleConsoleCancellationSource();

        throw new NotImplementedException();

        return ProcessRunHelpers.RunProcessAsync(new ProcessStartInfo("dotnet", "test"), tokenSource.Token, false)
            .Result.ExitCode;
    }
}
