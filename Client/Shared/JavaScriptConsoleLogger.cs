namespace ThriveDevCenter.Client.Shared
{
    using System;
    using Microsoft.Extensions.Logging;

    public class JavaScriptConsoleLogger : ILogger
    {
        private readonly string categoryName;

        public JavaScriptConsoleLogger(string categoryName)
        {
            this.categoryName = categoryName;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Console.WriteLine(categoryName + ": " + formatter(state, exception));
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state) => new DummyScope();

        private class DummyScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    public class JavaScriptConsoleLoggerProvider : ILoggerProvider
    {
        public void Dispose()
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new JavaScriptConsoleLogger(categoryName);
        }
    }
}
