using System;

namespace MeshTool.Core.Services
{
    /// <summary>
    /// Logging service interface for decoupled logging.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Logs a debug message.
        /// </summary>
        void Debug(string message);

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        void Info(string message);

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        void Warning(string message);

        /// <summary>
        /// Logs an error message.
        /// </summary>
        void Error(string message);

        /// <summary>
        /// Logs an error message with an exception.
        /// </summary>
        void Error(string message, Exception exception);

        /// <summary>
        /// Event raised when a new log entry is added.
        /// </summary>
        event EventHandler<LogEventArgs>? LogAdded;

        /// <summary>
        /// Clears the recent log entries.
        /// </summary>
        void ClearRecentLogs();
    }

    /// <summary>
    /// Arguments for log events.
    /// </summary>
    public class LogEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the log level.
        /// </summary>
        public LogLevel Level { get; }

        /// <summary>
        /// Gets the log message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the timestamp of the log entry.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Creates a new LogEventArgs instance.
        /// </summary>
        public LogEventArgs(LogLevel level, string message)
        {
            Level = level;
            Message = message;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Log level enumeration.
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
