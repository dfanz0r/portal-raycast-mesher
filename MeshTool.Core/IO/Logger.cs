using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace MeshTool.Core.IO
{
    /// <summary>
    /// Log severity levels for the Logger service.
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Thread-safe static logging service that supports multiple output targets.
    /// Logs to console, file, and provides events for UI subscribers.
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new();
        private static StreamWriter? _fileWriter;
        private static string? _logFilePath;
        private static LogLevel _minimumLevel = LogLevel.Debug;
        private static bool _includeTimestamps = true;
        private static readonly Queue<string> _recentLogs = new();
        private const int MaxRecentLogs = 500;

        /// <summary>
        /// Event raised when a new log message is written.
        /// </summary>
        public static event EventHandler<LogEventArgs>? LogAdded;

        /// <summary>
        /// Gets or sets the minimum log level. Messages below this level are ignored.
        /// </summary>
        public static LogLevel MinimumLevel
        {
            get => _minimumLevel;
            set => _minimumLevel = value;
        }

        /// <summary>
        /// Gets or sets whether timestamps are included in log messages.
        /// </summary>
        public static bool IncludeTimestamps
        {
            get => _includeTimestamps;
            set => _includeTimestamps = value;
        }

        /// <summary>
        /// Gets the current log file path, or null if file logging is not enabled.
        /// </summary>
        public static string? LogFilePath => _logFilePath;

        /// <summary>
        /// Gets the number of recent log entries stored in memory.
        /// </summary>
        public static int RecentLogCount
        {
            get
            {
                lock (_lock)
                {
                    return _recentLogs.Count;
                }
            }
        }

        /// <summary>
        /// Enables logging to a file. Creates the file and directory if they don't exist.
        /// </summary>
        /// <param name="filePath">Path to the log file. If null, generates a default path.</param>
        /// <returns>True if file logging was enabled successfully.</returns>
        public static bool EnableFileLogging(string? filePath = null)
        {
            lock (_lock)
            {
                try
                {
                    // Close existing writer if any
                    _fileWriter?.Dispose();
                    _fileWriter = null;

                    // Generate default path if not provided
                    _logFilePath = filePath ?? GetDefaultLogPath();

                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Open file for appending
                    _fileWriter = new StreamWriter(_logFilePath, append: true)
                    {
                        AutoFlush = true
                    };

                    // Write session header
                    _fileWriter.WriteLine($"=== Log session started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

                    return true;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to enable file logging: {ex.Message}");
                    _logFilePath = null;
                    _fileWriter?.Dispose();
                    _fileWriter = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// Disables file logging and closes the log file.
        /// </summary>
        public static void DisableFileLogging()
        {
            lock (_lock)
            {
                if (_fileWriter != null)
                {
                    _fileWriter.WriteLine($"=== Log session ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    _fileWriter.Dispose();
                    _fileWriter = null;
                }
                _logFilePath = null;
            }
        }

        /// <summary>
        /// Gets the default log file path in the application data folder.
        /// </summary>
        public static string GetDefaultLogPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDirectory = Path.Combine(appDataPath, "MeshTool", "Logs");
            var logFileName = $"meshtool_{DateTime.Now:yyyyMMdd}.log";
            return Path.Combine(logDirectory, logFileName);
        }

        /// <summary>
        /// Logs a debug message.
        /// </summary>
        public static void Debug(string message) => Log(LogLevel.Debug, message);

        /// <summary>
        /// Writes a progress dot to the console without any formatting.
        /// Used for CLI-style progress indicators during long operations.
        /// Does not log to file or raise events.
        /// </summary>
        public static void WriteProgressDot()
        {
            try
            {
                Console.Write(".");
                Console.Out.Flush();
            }
            catch
            {
                // Console may not be available in some contexts
            }
        }

        /// <summary>
        /// Logs an info message.
        /// </summary>
        public static void Info(string message) => Log(LogLevel.Info, message);

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        public static void Warning(string message) => Log(LogLevel.Warning, message);

        /// <summary>
        /// Logs an error message.
        /// </summary>
        public static void Error(string message) => Log(LogLevel.Error, message);

        /// <summary>
        /// Logs an error message with exception details.
        /// </summary>
        public static void Error(string message, Exception exception)
        {
            Log(LogLevel.Error, $"{message}: {exception.Message}");
            Log(LogLevel.Debug, $"Stack trace: {exception.StackTrace}");
        }

        /// <summary>
        /// Logs a message with the specified level.
        /// </summary>
        public static void Log(LogLevel level, string message)
        {
            // Check minimum level
            if (level < _minimumLevel)
                return;

            // Format the message
            var timestamp = _includeTimestamps ? $"[{DateTime.Now:HH:mm:ss.fff}] " : "";
            var levelPrefix = GetLevelPrefix(level);
            var formattedMessage = $"{timestamp}{levelPrefix}{message}";

            // Store in recent logs queue
            lock (_lock)
            {
                _recentLogs.Enqueue(formattedMessage);
                while (_recentLogs.Count > MaxRecentLogs)
                {
                    _recentLogs.Dequeue();
                }
            }

            // Write to console
            WriteToConsole(level, formattedMessage);

            // Write to file
            WriteToFile(formattedMessage);

            // Raise event for UI subscribers
            OnLogAdded(level, formattedMessage);
        }

        private static string GetLevelPrefix(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => "[DBG] ",
                LogLevel.Info => "[INF] ",
                LogLevel.Warning => "[WRN] ",
                LogLevel.Error => "[ERR] ",
                _ => ""
            };
        }

        private static void WriteToConsole(LogLevel level, string message)
        {
            try
            {
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = level switch
                {
                    LogLevel.Debug => ConsoleColor.Gray,
                    LogLevel.Info => ConsoleColor.White,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Error => ConsoleColor.Red,
                    _ => ConsoleColor.White
                };
                Console.WriteLine(message);
                Console.ForegroundColor = originalColor;
            }
            catch
            {
                // Console may not be available in some contexts
            }
        }

        private static void WriteToFile(string message)
        {
            lock (_lock)
            {
                try
                {
                    _fileWriter?.WriteLine(message);
                }
                catch
                {
                    // Ignore file write errors
                }
            }
        }

        private static void OnLogAdded(LogLevel level, string message)
        {
            LogAdded?.Invoke(null, new LogEventArgs(level, message));
        }

        /// <summary>
        /// Gets all recent log entries.
        /// </summary>
        public static string[] GetRecentLogs()
        {
            lock (_lock)
            {
                return _recentLogs.ToArray();
            }
        }

        /// <summary>
        /// Clears all recent log entries from memory.
        /// </summary>
        public static void ClearRecentLogs()
        {
            lock (_lock)
            {
                _recentLogs.Clear();
            }
        }

        /// <summary>
        /// Shuts down the logger and closes any open file handles.
        /// </summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                if (_fileWriter != null)
                {
                    _fileWriter.WriteLine($"=== Log session ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    _fileWriter.Dispose();
                    _fileWriter = null;
                }
            }
        }
    }

    /// <summary>
    /// Event arguments for log events.
    /// </summary>
    public class LogEventArgs : EventArgs
    {
        public LogLevel Level { get; }
        public string Message { get; }
        public DateTime Timestamp { get; }

        public LogEventArgs(LogLevel level, string message)
        {
            Level = level;
            Message = message;
            Timestamp = DateTime.Now;
        }
    }
}
