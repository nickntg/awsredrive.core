using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using NLog;

namespace AWSRedrive
{
    public class QueueProcessor : IQueueProcessor
    {
        private const int MaxMessageContentSize = 100 * 1024; // 100KB
        private static readonly TimeSpan LogLevelTimeout = TimeSpan.FromMinutes(30);

        public ConfigurationEntry Configuration { get; set; }

        private readonly IMetricsSettings _metricsSettings;
        private readonly string _defaultLogLevel;
        private readonly Logger _metricsLogger;
        private readonly Logger _entryLogger;

        private EntryLogger _logger;
        private IQueueClient _queueClient;
        private IMessageProcessorFactory _messageProcessorFactory;
        private Task _task;
        private CancellationTokenSource _cancellation;
        private QueueMetrics _metrics;
        private DateTime _lastMetricsLog;

        // Time-limited log level tracking
        private DateTime? _logLevelChangedAt;
        private string _originalLogLevel;

        public QueueProcessor(IMetricsSettings metricsSettings, string defaultLogLevel = "Error")
        {
            _metricsSettings = metricsSettings ?? new MetricsSettingsProvider(null);
            _defaultLogLevel = defaultLogLevel ?? "Error";
            _metricsLogger = LogManager.GetLogger("Metrics");
            _entryLogger = LogManager.GetLogger("Entry");
        }

        public QueueProcessor() : this(null, "Error") { }

        public void Init(IQueueClient queueClient, 
            IMessageProcessorFactory messageProcessorFactory, 
            ConfigurationEntry configuration)
        {
            Configuration = configuration;
            _queueClient = queueClient;
            _messageProcessorFactory = messageProcessorFactory;
            
            var effectiveLogLevel = configuration.LogLevel ?? _defaultLogLevel;
            _logger = new EntryLogger(configuration.Alias, effectiveLogLevel);
            _originalLogLevel = effectiveLogLevel;
            
            _metrics = MetricsStore.GetOrCreate(configuration.Alias);
            _lastMetricsLog = DateTime.UtcNow;
        }

        public void SetLogLevel(string level)
        {
            if (_logLevelChangedAt == null)
            {
                _originalLogLevel = _logger.CurrentLogLevel;
            }
            _logLevelChangedAt = DateTime.UtcNow;
            _logger.SetLogLevel(level);
            Configuration.LogLevel = level;
            _logger.Info($"Log level changed to {level} (reverts in 30 min)");
        }

        public string GetLogLevel()
        {
            return _logger.CurrentLogLevel;
        }

        public void Start()
        {
            if (_task != null)
            {
                _logger.Debug("Already started");
                return;
            }

            _logger.Info($"Starting processor for queue {Configuration.QueueUrl}");
            _metrics.StartedAt = DateTime.UtcNow;
            _cancellation = new CancellationTokenSource();
            _task = new Task(ProcessMessageLoop, _cancellation.Token, TaskCreationOptions.LongRunning);
            _task.Start();

            if (_metricsSettings.Enabled)
            {
                LogMetrics("STARTED");
            }
        }

        public void Stop()
        {
            if (_task == null)
            {
                _logger.Debug("Already stopped");
                return;
            }

            _logger.Info($"Stopping processor for queue {Configuration.QueueUrl}");

            try
            {
                _cancellation.Cancel();
                Task.WaitAll(new[] { _task }, 30 * 1000);
                _cancellation.Dispose();
                _task.Dispose();
            }
            catch (Exception e)
            {
                _logger.Warn($"Not stopped gracefully - {e.Message}");
            }
            finally
            {
                _task = null;

                if (_metricsSettings.Enabled)
                {
                    LogMetrics("STOPPED");
                }
            }
        }

        public void ProcessMessageLoop()
        {
            _logger.Debug("Entering message loop");
            
            while (!_cancellation.IsCancellationRequested)
            {
                CheckLogLevelExpiry();
                CheckPeriodicMetrics();

                IMessage msg;

                try
                {
                    _logger.Trace("Polling for message...");
                    msg = _queueClient.GetMessage();
                    if (msg == null)
                    {
                        _logger.Trace("No message received (timeout)");
                        continue;
                    }
                }
                catch (Exception e)
                {
                    LogError("Queue receive error", e);
                    _metrics.LastError = DateTime.UtcNow;
                    _metrics.LastErrorMessage = e.Message;
                    continue;
                }

                _metrics.MessagesReceived++;
                _metrics.LastMessageReceived = DateTime.UtcNow;
                _metrics.LastMessageContent = TruncateMessage(msg.Content);

                // Use scoped logger with messageId for all message-related logs
                var msgLogger = _logger.WithMessageId(msg.MessageId);
                
                msgLogger.Debug("Message received");
                
                // Log attributes at Debug level
                if (msg.Attributes?.Count > 0)
                {
                    msgLogger.Debug($"Attributes ({msg.Attributes.Count}): {string.Join(", ", msg.Attributes.Select(a => $"{a.Key}={TruncateForLog(a.Value, 100)}"))}");
                }
                
                // Log content at Trace level
                if (msgLogger.IsTraceEnabled)
                {
                    msgLogger.Trace($"Content ({msg.Content?.Length ?? 0} chars): {TruncateForLog(msg.Content, 1000)}");
                }

                try
                {
                    var target = Configuration.RedriveUrl ?? Configuration.RedriveScript ?? Configuration.RedriveKafkaTopic ?? "unknown";
                    msgLogger.Debug($"Processing to {target}");
                    
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var processor = _messageProcessorFactory.CreateMessageProcessor(Configuration);
                    processor.ProcessMessage(msg.Content, msg.Attributes, Configuration, msgLogger);
                    sw.Stop();
                    
                    msgLogger.Debug($"Processed successfully in {sw.ElapsedMilliseconds}ms");

                    _metrics.MessagesSent++;
                    _metrics.LastMessageSent = DateTime.UtcNow;

                    try
                    {
                        msgLogger.Trace("Deleting from queue");
                        _queueClient.DeleteMessage(msg);
                        msgLogger.Debug("Deleted");
                    }
                    catch (Exception e)
                    {
                        LogError("Delete failed", e, msg.MessageId, null, msg.Attributes);
                        _metrics.LastError = DateTime.UtcNow;
                        _metrics.LastErrorMessage = e.Message;
                    }
                }
                catch (Exception e)
                {
                    _metrics.MessagesFailed++;
                    _metrics.LastError = DateTime.UtcNow;
                    _metrics.LastErrorMessage = e.Message;

                    LogError("Process failed", e, msg.MessageId, msg.Content, msg.Attributes);
                }
            }
            
            _logger.Debug("Exiting message loop");
        }

        private void CheckLogLevelExpiry()
        {
            if (_logLevelChangedAt.HasValue &&
                DateTime.UtcNow - _logLevelChangedAt.Value > LogLevelTimeout)
            {
                _logger.Info($"Log level reverting to {_originalLogLevel} after timeout");
                _logger.SetLogLevel(_originalLogLevel);
                Configuration.LogLevel = _originalLogLevel;
                _logLevelChangedAt = null;
            }
        }

        private static string TruncateMessage(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            if (content.Length <= MaxMessageContentSize)
                return content;

            return content.Substring(0, MaxMessageContentSize) + $"... [truncated, original size: {content.Length:N0} bytes]";
        }

        private static string TruncateForLog(string content, int maxLength)
        {
            if (string.IsNullOrEmpty(content))
                return "(empty)";

            if (content.Length <= maxLength)
                return content;

            return content.Substring(0, maxLength) + "...";
        }

        private void CheckPeriodicMetrics()
        {
            if (!_metricsSettings.Enabled) return;

            var now = DateTime.UtcNow;
            if ((now - _lastMetricsLog).TotalSeconds >= _metricsSettings.IntervalSeconds)
            {
                LogMetrics("METRICS");
                _lastMetricsLog = now;
            }
        }

        private void LogMetrics(string eventType)
        {
            var uptime = DateTime.UtcNow - _metrics.StartedAt;

            var evt = new LogEventInfo(LogLevel.Info, "Metrics", eventType);
            evt.Properties["alias"] = Configuration.Alias;
            evt.Properties["eventType"] = eventType;
            evt.Properties["uptimeSeconds"] = (int)uptime.TotalSeconds;
            evt.Properties["messagesReceived"] = _metrics.MessagesReceived;
            evt.Properties["messagesSent"] = _metrics.MessagesSent;
            evt.Properties["messagesFailed"] = _metrics.MessagesFailed;
            evt.Properties["queueUrl"] = Configuration.QueueUrl;
            evt.Properties["region"] = Configuration.Region;
            evt.Properties["redriveUrl"] = Configuration.RedriveUrl;
            evt.Properties["logLevel"] = _logger.CurrentLogLevel;

            _metricsLogger.Log(evt);
        }

        private void LogError(string message, Exception ex, string messageId = null, string messageContent = null, Dictionary<string, string> attributes = null)
        {
            var loggerName = "QueueProcessor";
            var logger = LogManager.GetLogger(loggerName);
            
            var evt = new LogEventInfo(LogLevel.Error, loggerName, message);
            evt.Properties["alias"] = Configuration.Alias;
            evt.Properties["queueUrl"] = Configuration.QueueUrl;
            evt.Properties["region"] = Configuration.Region;
            evt.Properties["redriveUrl"] = Configuration.RedriveUrl;
            evt.Properties["errorType"] = ex.GetType().Name;
            evt.Properties["errorMessage"] = ex.Message;
            if (!string.IsNullOrEmpty(messageId))
            {
                evt.Properties["messageId"] = messageId;
            }
            evt.Exception = ex;

            if (messageContent != null)
            {
                evt.Properties["messageContent"] = TruncateMessage(messageContent);
            }

            if (attributes != null && attributes.Count > 0)
            {
                evt.Properties["messageAttributes"] = string.Join(", ", attributes.Select(a => $"{a.Key}={TruncateForLog(a.Value, 100)}"));
            }

            logger.Log(evt);
        }
    }
}