using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.CommandLine.Logging;

/// <summary>
/// <b>空日志提供程序</b>
/// </summary>
internal sealed class NullLoggerProvider : ILoggerProvider
{
    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

    /// <inheritdoc />
    public void Dispose() { }

    /// <summary>
    /// <b>空日志器</b>
    /// </summary>
    private sealed class NullLogger : ILogger
    {
        /// <summary>
        /// 单例实例。
        /// </summary>
        public static readonly NullLogger Instance = new();

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Shared;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => false;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }

        /// <summary>
        /// <b>空作用域</b>
        /// </summary>
        private sealed class NullScope : IDisposable
        {
            /// <summary>
            /// 单例实例。
            /// </summary>
            public static readonly NullScope Shared = new();

            /// <inheritdoc />
            public void Dispose() { }
        }
    }
}

