using System;
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

        public ConfigurationEntry Configuration { get; set; }

        private readonly IMetricsSettings _metricsSettings;
        private readonly Logger _metricsLogger;
        private readonly Logger _entryLogger;

        private EntryLogger _logger;
        private IQueueClient _queueClient;
        private IMessageProcessorFactory _messageProcessorFactory;
        private Task _task;
        private CancellationTokenSource _cancellation;
        private QueueMetrics _metrics;
        private DateTime _lastMetricsLog;

        public QueueProcessor(IMetricsSettings metricsSettings)
        {
            _metricsSettings = metricsSettings ?? new MetricsSettingsProvider(null);
            _metricsLogger = LogManager.GetLogger("Metrics");
            _entryLogger = LogManager.GetLogger("Entry");
        }

        public QueueProcessor() : this(null) { }

        public void Init(IQueueClient queueClient, 
            IMessageProcessorFactory messageProcessorFactory, 
            ConfigurationEntry configuration)
        {
            Configuration = configuration;
            _queueClient = queueClient;
            _messageProcessorFactory = messageProcessorFactory;
            _logger = new EntryLogger(configuration.Alias, configuration.LogLevel);
            _metrics = MetricsStore.GetOrCreate(configuration.Alias);
            _lastMetricsLog = DateTime.UtcNow;
        }

        public void SetLogLevel(string level)
        {
            _logger.SetLogLevel(level);
            Configuration.LogLevel = level;
            _logger.Info($"Log level changed to {level}");
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

                _logger.Debug("Message received");
                if (_logger.IsTraceEnabled)
                {
                    _logger.Trace($"Content: {msg.Content}");
                }

                try
                {
                    _logger.Debug($"Processing to {Configuration.RedriveUrl}");
                    var processor = _messageProcessorFactory.CreateMessageProcessor(Configuration);
                    processor.ProcessMessage(msg.Content, msg.Attributes, Configuration, _logger);
                    _logger.Debug("Processed");

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
                        LogError($"Delete failed [{msg.MessageIdentifier}]", e);
                        _metrics.LastError = DateTime.UtcNow;
                        _metrics.LastErrorMessage = e.Message;
                    }
                }
                catch (Exception e)
                {
                    _metrics.MessagesFailed++;
                    _metrics.LastError = DateTime.UtcNow;
                    _metrics.LastErrorMessage = e.Message;

                    LogError($"Process failed [{msg.MessageIdentifier}]", e, msg.Content);
                }
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

        private void LogError(string message, Exception ex, string messageContent = null)
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
            evt.Exception = ex;

            if (messageContent != null)
            {
                evt.Properties["messageContent"] = TruncateMessage(messageContent);
            }

            logger.Log(evt);
        }
    }
}
