using System;
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

            try
            {
                _cancellation.Cancel();
                Task.WaitAll(new[] { _task }, 30 * 1000);
                _cancellation.Dispose();
                _task.Dispose();
            }
            catch (Exception e)
            {
                _logger.Warn($"Not stopped gracefully - {e}");
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
            while (!_cancellation.IsCancellationRequested)
            {
                CheckLogLevelExpiry();
                CheckPeriodicMetrics();

                IMessage msg;

                try
                {
                    _logger.Debug("Waiting for message");
                    msg = _queueClient.GetMessage();
                    if (msg == null)
                    {
                        _logger.Debug("No message");
                        continue;
                    }
                }
                catch (Exception e)
                {
                    LogError("Queue error", e);
                    _metrics.LastError = DateTime.UtcNow;
                    _metrics.LastErrorMessage = e.Message;
                    continue;
                }

                _metrics.MessagesReceived++;
                _metrics.LastMessageReceived = DateTime.UtcNow;
                _metrics.LastMessageContent = TruncateMessage(msg.Content);

                _logger.Debug($"Message received [id={msg.MessageIdentifier}]");
                if (_logger.IsTraceEnabled)
                {
                    _logger.Trace($"Message content ({msg.Content?.Length ?? 0} chars): {msg.Content}");
                    if (msg.Attributes?.Count > 0)
                    {
                        _logger.Trace($"Message attributes: {string.Join(", ", msg.Attributes.Select(a => $"{a.Key}={a.Value}"))}");
                    }
                }

                try
                {
                    var target = Configuration.RedriveUrl ?? Configuration.RedriveScript ?? Configuration.RedriveKafkaTopic ?? "unknown";
                    _logger.Debug($"Processing message [id={msg.MessageIdentifier}] to {target}");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var processor = _messageProcessorFactory.CreateMessageProcessor(Configuration);
                    var scopedLogger = _logger.WithMessageId(msg.MessageIdentifier);
                    processor.ProcessMessage(msg.Content, msg.Attributes, Configuration, scopedLogger);
                    sw.Stop();
                    _logger.Debug($"Processed [id={msg.MessageIdentifier}] in {sw.ElapsedMilliseconds}ms");

                    _metrics.MessagesSent++;
                    _metrics.LastMessageSent = DateTime.UtcNow;

                    try
                    {
                        _logger.Debug($"Deleting [{msg.MessageIdentifier}]");
                        _queueClient.DeleteMessage(msg);
                        _logger.Debug("Deleted");
                    }
                    catch (Exception e)
                    {
                        LogError($"Delete failed", e, msg.MessageIdentifier);
                        _metrics.LastError = DateTime.UtcNow;
                        _metrics.LastErrorMessage = e.Message;
                    }
                }
                catch (Exception e)
                {
                    _metrics.MessagesFailed++;
                    _metrics.LastError = DateTime.UtcNow;
                    _metrics.LastErrorMessage = e.Message;

                    LogError($"Process failed", e, msg.MessageIdentifier, msg.Content);
                }
            }
        }

        private void CheckLogLevelExpiry()
        {
            if (_logLevelChangedAt.HasValue &&
                DateTime.UtcNow - _logLevelChangedAt.Value > LogLevelTimeout)
            {
                _logger.SetLogLevel(_originalLogLevel);
                Configuration.LogLevel = _originalLogLevel;
                _logger.Info($"Log level reverted to {_originalLogLevel} after timeout");
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

        private void LogError(string message, Exception ex, string messageId = null, string messageContent = null)
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

            logger.Log(evt);
        }
    }
}