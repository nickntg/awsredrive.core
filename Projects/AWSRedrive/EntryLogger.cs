using System;
using System.IO;
using System.Runtime.CompilerServices;
using NLog;

namespace AWSRedrive
{
    public class EntryLogger
    {
        private volatile LogLevel _minLevel;
        private readonly string _alias;

        public EntryLogger(string alias, string logLevel)
        {
            _alias = alias;
            _minLevel = ParseLogLevel(logLevel);
        }

        public string CurrentLogLevel => _minLevel.Name;

        public void SetLogLevel(string level)
        {
            _minLevel = ParseLogLevel(level);
        }

        private static LogLevel ParseLogLevel(string level)
        {
            if (string.IsNullOrWhiteSpace(level))
                return LogLevel.Error;

            try
            {
                return LogLevel.FromString(level);
            }
            catch
            {
                return LogLevel.Error;
            }
        }

        private bool IsEnabled(LogLevel level) => level >= _minLevel;

        public bool IsTraceEnabled => IsEnabled(LogLevel.Trace);
        public bool IsDebugEnabled => IsEnabled(LogLevel.Debug);
        public bool IsInfoEnabled => IsEnabled(LogLevel.Info);

        private void Log(LogLevel level, string message, Exception ex, string sourceFilePath)
        {
            if (!IsEnabled(level)) return;

            var loggerName = string.IsNullOrEmpty(sourceFilePath) 
                ? "Unknown" 
                : Path.GetFileNameWithoutExtension(sourceFilePath);

            var logger = LogManager.GetLogger(loggerName);
            var evt = new LogEventInfo(level, loggerName, message);
            evt.Properties["alias"] = _alias;
            if (ex != null) evt.Exception = ex;
            logger.Log(evt);
        }

        public void Trace(string message, [CallerFilePath] string sourceFilePath = "") 
            => Log(LogLevel.Trace, message, null, sourceFilePath);

        public void Debug(string message, [CallerFilePath] string sourceFilePath = "") 
            => Log(LogLevel.Debug, message, null, sourceFilePath);

        public void Info(string message, [CallerFilePath] string sourceFilePath = "") 
            => Log(LogLevel.Info, message, null, sourceFilePath);

        public void Warn(string message, [CallerFilePath] string sourceFilePath = "") 
            => Log(LogLevel.Warn, message, null, sourceFilePath);

        public void Warn(Exception ex, string message, [CallerFilePath] string sourceFilePath = "") 
            => Log(LogLevel.Warn, message, ex, sourceFilePath);

        public void Error(string message, [CallerFilePath] string sourceFilePath = "") 
            => Log(LogLevel.Error, message, null, sourceFilePath);

        public void Error(Exception ex, string message, [CallerFilePath] string sourceFilePath = "") 
            => Log(LogLevel.Error, message, ex, sourceFilePath);

        public void Fatal(string message, [CallerFilePath] string sourceFilePath = "") 
            => Log(LogLevel.Fatal, message, null, sourceFilePath);

        public void Fatal(Exception ex, string message, [CallerFilePath] string sourceFilePath = "") 
            => Log(LogLevel.Fatal, message, ex, sourceFilePath);
    }
}
