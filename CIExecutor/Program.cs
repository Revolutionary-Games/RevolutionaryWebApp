namespace CIExecutor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Extensions.Logging;
using RevolutionaryWebApp.Server.Common.Services;
using SharedBase.Utilities;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await Parser.Default.ParseArguments<Options>(args)
            .MapResult(RunOptions,
                ReportParseErrors);
    }

    private static Task<int> ReportParseErrors(IEnumerable<Error> errors)
    {
        try
        {
            Console.WriteLine("Failed to parse command line arguments:");
            foreach (var error in errors)
            {
                Console.WriteLine(error.Tag);
                Console.WriteLine(error.ToString());
            }

            return Task.FromResult(3);
        }
        catch (Exception exception)
        {
            return Task.FromException<int>(exception);
        }
    }

    private static async Task<int> RunOptions(Options options)
    {
        if (options.Version)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;

            Console.WriteLine($"CIExecutor version: {version}");
            return 0;
        }

        // Load from environment variables if secrets are specified like that
        if (options.SecretKey == "-")
        {
            options.SecretKey = Environment.GetEnvironmentVariable("RUNNER_SECRET_KEY") ?? string.Empty;
        }

        if (options.ConnectionKey == "-")
        {
            options.ConnectionKey = Environment.GetEnvironmentVariable("RUNNER_ACCESS_KEY") ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            Console.WriteLine("Secret key is not specified (and missing environment variable 'RUNNER_SECRET_KEY')");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionKey))
        {
            Console.WriteLine("Connection key is not specified (and missing environment variable " +
                "'RUNNER_ACCESS_KEY')");
            return 1;
        }

        var baseLogger = new ConsoleCategoryLogger<Program>();

        baseLogger.LogInformation("Starting service construction...");

        var executor = new JobExecutor(new ConsoleCategoryLogger<JobExecutor>());

        if (options.Verbose)
        {
            executor.Verbose = true;
            ConsolePrefixLogger.MinimumLevel = LogLevel.Trace;
        }

        var cacheFolder = options.CacheLocation;

        if (string.IsNullOrWhiteSpace(cacheFolder))
        {
            // TODO: detect the actual username as a fallback here
            var home = Environment.GetEnvironmentVariable("HOME") ?? "/home/runner";

            cacheFolder = Path.Join(home, "runnerCache");
        }

        using var cache =
            new FilesystemAndPodmanCache(new ConsoleCategoryLogger<FilesystemAndPodmanCache>(), cacheFolder);

        using var communication =
            new RunnerClientWebsocket(new ConsoleCategoryLogger<RunnerClientWebsocket>(), options.ServerUrl);

        var runnerService = new RunnerService(new ConsoleCategoryLogger<RunnerService>(), communication, options,
            executor, cache, !options.InteractiveMode);

        // Apply some runner options
        if (options.QuitOnIdle)
            runnerService.StopWhenIdle();

        if (options.OnlySafe)
            runnerService.OnlyRunSafeJobs();

        using var cancellationListener = new ProgramTerminationController(runnerService.StopAfterNextJob);

        baseLogger.LogInformation("Starting executor main loop");

        var result = await runnerService.Run(cancellationListener.Token);

        baseLogger.LogInformation("Executor has finished, saving final things and exiting");

        await cache.SaveUsedPodmanImages();

        try
        {
            await communication.Close().WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (Exception e)
        {
            baseLogger.LogError(e, "Failed to close runner connection when exiting");
        }

        return result;
    }

    public class Options : IRunnerClientDataService
    {
        [Option('v', "version", Required = false, HelpText = "Print version and quit.")]
        public bool Version { get; set; }

        [Option("devcenter", Required = false, HelpText = "The dev center url to use.")]
        public string DevCenterUrl { get; set; } = "https://dev.revolutionarygamesstudio.com/";

        [Option('k', "key", Required = true, HelpText = "Access token to use for the dev center.")]
        public string ConnectionKey { get; set; } = string.Empty;

        [Option('s', "secret", Required = true, HelpText = "Connection secret for the dev center.")]
        public string SecretKey { get; set; } = string.Empty;

        [Option('q', "quit-on-idle", Default = false,
            HelpText = "If specified, will run available jobs and then quit.")]
        public bool QuitOnIdle { get; set; }

        [Option('d', "disk", HelpText = "Max disk usage for local caches.")]
        public long MaxCacheSize { get; set; } = (long)GlobalConstants.GIBIBYTE * 130;

        // TODO: implement cache retaining
        // public long KeepCacheSize { get; set; } = (long)GlobalConstants.GIBIBYTE * 5;
        [Option('r', "retained-cache", HelpText = "How much cache to keep on cleaning.")]
        public long KeepCacheSize { get; set; } = 0;

        [Option('p', "prune-cache-fraction", HelpText = "How big fraction of the cache size in use causes pruning.")]
        public float PruneCacheAfterSizeFraction { get; set; } = 0.75f;

        [Option('c', "cache", HelpText = "Specifies where to put caches. Defaults to current user home")]
        public string? CacheLocation { get; set; }

        [Option('i', "interactive", Default = false,
            HelpText = "Assume interactive terminal controls this and don't react to terminal size changes")]
        public bool InteractiveMode { get; set; }

        [Option("verbose", Default = false, HelpText = "Turn on verbose logging and job output.")]
        public bool Verbose { get; set; }

        [Option("safe-only", Default = false, HelpText = "Can be set to only run 'safe' jobs")]
        public bool OnlySafe { get; set; }

        public string ServerUrl => new Uri(new Uri(DevCenterUrl), "runnerConnection").ToString();
    }
}
