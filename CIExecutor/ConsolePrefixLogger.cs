using System;
using System.Threading;
using Microsoft.Extensions.Logging;

public class ConsolePrefixLogger(string categoryName) : ILogger
{
    /// <summary>
    ///   Used to prevent mixing up output from multiple threads
    /// </summary>
    private static readonly SemaphoreSlim ConsoleOutputLock = new(1, 1);

    /// <summary>
    ///   Used to configure verbosity of the logger
    /// </summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= MinimumLevel;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        string message = formatter(state, exception);
        string prefix = logLevel switch
        {
            LogLevel.Error or LogLevel.Critical => " ERROR: ",
            LogLevel.Warning => " WARNING: ",
            _ => " ",
        };

        ConsoleOutputLock.Wait();
        try
        {
            Console.WriteLine($"[{categoryName}]{prefix}{message}");

            if (exception != null)
            {
                Console.WriteLine(exception);
            }
        }
        finally
        {
            ConsoleOutputLock.Release();
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

public sealed class ConsolePrefixLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new ConsolePrefixLogger(categoryName);
    }

    public void Dispose()
    {
    }
}

public sealed class ConsoleCategoryLogger<T>()
    : ConsolePrefixLogger(typeof(T).Name.Replace("`1", string.Empty)), ILogger<T>;
