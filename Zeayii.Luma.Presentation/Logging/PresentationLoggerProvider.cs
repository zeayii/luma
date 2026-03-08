using Zeayii.Luma.Abstractions.Abstractions;
using Zeayii.Luma.Abstractions.Models;
using Zeayii.Luma.Presentation.Configuration;
using Microsoft.Extensions.Logging;

namespace Zeayii.Luma.Presentation.Logging;

/// <summary>
/// <b>Presentation 日志提供程序</b>
/// </summary>
public sealed class PresentationLoggerProvider(ILogManager logManager, PresentationOptions options) : ILoggerProvider
{
    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return new PresentationLogger(categoryName, logManager, options);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>
    /// <b>Presentation 日志器</b>
    /// </summary>
    private sealed class PresentationLogger(string categoryName, ILogManager logManager, PresentationOptions options) : ILogger
    {
        /// <summary>
        /// 日志分类名。
        /// </summary>
        private readonly string _categoryName = string.IsNullOrWhiteSpace(categoryName) ? "App" : categoryName;

        /// <summary>
        /// 日志管理器。
        /// </summary>
        private readonly ILogManager _logManager = logManager;

        /// <summary>
        /// 呈现配置。
        /// </summary>
        private readonly PresentationOptions _options = options;

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Shared;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel >= _options.MinimumLogLevel;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            var message = formatter(state, exception);
            _logManager.Write(Map(logLevel), _categoryName, message, exception);
        }

        /// <summary>
        /// 映射日志等级。
        /// </summary>
        private static LogLevelKind Map(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => LogLevelKind.Trace,
                LogLevel.Debug => LogLevelKind.Debug,
                LogLevel.Information => LogLevelKind.Information,
                LogLevel.Warning => LogLevelKind.Warning,
                LogLevel.Error => LogLevelKind.Error,
                LogLevel.Critical => LogLevelKind.Critical,
                _ => LogLevelKind.Information
            };
        }

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
