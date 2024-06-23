using Microsoft.Extensions.Logging;

namespace harness
{
    public class ConsoleWritelineLogger : ILogger
    {
        public static readonly ConsoleWritelineLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Console.ForegroundColor = colours[logLevel];
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + formatter(state, exception));
            Console.ResetColor();
        }

        private static readonly Dictionary<LogLevel, ConsoleColor> colours = new()
        {
            { LogLevel.Trace, ConsoleColor.Gray },
            { LogLevel.Debug, ConsoleColor.Gray },
            { LogLevel.Information, ConsoleColor.White },
            { LogLevel.Warning, ConsoleColor.Yellow },
            { LogLevel.Error, ConsoleColor.Red },
            { LogLevel.Critical, ConsoleColor.Red }
        };
    }
}