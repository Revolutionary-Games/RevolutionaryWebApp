namespace CIExecutor;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CommandLine;
using RevolutionaryWebApp.Server.Common.Services;
using SharedBase.Utilities;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // TODO: command line parsing
        /*return Parser.Default.ParseArguments<Options>(args)
            .MapResult(RunOptions,
                ReportParseErrors);*/

        // This fails with an exception because we can't anyway report our failure if we don't have
        // the webhook connection url
        if (args.Length != 1)
            throw new Exception("Expected to be ran with a single argument specifying websocket connect url");

        using var executor = new CIExecutor(args[0]);

        await executor.Run();
        return 0;
    }

    private static int ReportParseErrors(IEnumerable<Error> errors)
    {
        Console.WriteLine("Failed to parse command line arguments:");
        foreach (var error in errors)
        {
            Console.WriteLine(error.Tag);
            Console.WriteLine(error.ToString());
        }

        return 3;
    }

    private static int RunOptions(Options options)
    {
        if (options.Version)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;

            Console.WriteLine($"CIExecutor version: {version}");
            return 0;
        }

        // TODO: new main here
        throw new NotImplementedException();
    }

    public class Options : IRunnerClientDataService
    {
        [Option('v', "version", Required = false, HelpText = "Print version and quit.")]
        public bool Version { get; set; }

        [Option('d', "devcenter", Required = false, HelpText = "The dev center url to use.")]
        public string DevCenterUrl { get; set; } = "https://dev.revolutionarygamesstudio.com/";

        [Option('k', "key", Required = true, HelpText = "Access token to use for the dev center.")]
        public string ConnectionKey { get; set; } = string.Empty;

        [Option('s', "secret", Required = true, HelpText = "Connection secret for the dev center.")]
        public string SecretKey { get; set; } = string.Empty;

        [Option('q', "quit-on-idle", Default = false,
            HelpText = "If specified, will run available jobs and then quit.")]
        public bool QuitOnIdle { get; set; }

        [Option('d', "disk", Required = true, HelpText = "Max disk usage for local caches.")]
        public long MaxCacheSize { get; set; } = (long)GlobalConstants.GIBIBYTE * 150;

        [Option('r', "retained-cache", Required = true, HelpText = "How much cache to keep on cleaning.")]
        public long KeepCacheSize { get; set; } = (long)GlobalConstants.GIBIBYTE * 5;

        [Option('p', "prune-cache-fraction", Required = true,
            HelpText = "How big fraction of the cache size in use causes pruning.")]
        public float PruneCacheAfterSizeFraction { get; set; } = 0.8f;

        /*
        // TODO: implement this mode
        [Option("safe-only", Default = false, HelpText = "Can be set to only run 'safe' jobs")]
        public bool OnlySafe { get; set; }*/

        public string ServerUrl => new Uri(new Uri(DevCenterUrl), "runnerConnection").ToString();
    }
}
