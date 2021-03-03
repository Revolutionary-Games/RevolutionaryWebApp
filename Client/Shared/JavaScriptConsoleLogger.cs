namespace ThriveDevCenter.Client.Shared
{
    using System;
    using Microsoft.Extensions.Logging;

    public class JavaScriptConsoleLogger : ILogger
    {
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            Console.WriteLine(formatter(state, exception));
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state) => default;
    }

    public class JavaScriptConsoleLoggerProvider : ILoggerProvider
    {
        public void Dispose()
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            // TODO: maybe could preserve the category as a prefix
            return new JavaScriptConsoleLogger();
        }
    }
}
