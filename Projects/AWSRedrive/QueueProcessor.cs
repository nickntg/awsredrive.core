using System;
using System.Threading;
using System.Threading.Tasks;
using AWSRedrive.Interfaces;
using NLog;

namespace AWSRedrive
{
    public class QueueProcessor : IQueueProcessor
    {
        public ConfigurationEntry Configuration { get; set; }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private IQueueClient _queueClient;
        private IMessageProcessorFactory _messageProcessorFactory;
        private Task _task;
        private CancellationTokenSource _cancellation;
        private int _messagesReceived;
        private int _messagesSent;
        private int _messagesFailed;

        public void Init(IQueueClient queueClient, 
            IMessageProcessorFactory messageProcessorFactory, 
            ConfigurationEntry configuration)
        {
            Configuration = configuration;
            _queueClient = queueClient;
            _messageProcessorFactory = messageProcessorFactory;
        }

        public void Start()
        {
            if (_task != null)
            {
                Logger.Info($"Queue processor [{Configuration.Alias}] is already started");
                return;
            }

            _cancellation = new CancellationTokenSource();
            _task = new Task(ProcessMessageLoop, _cancellation.Token, TaskCreationOptions.LongRunning);
            _task.Start();
        }

        public void Stop()
        {
            if (_task == null)
            {
                Logger.Info($"Queue processor [{Configuration.Alias}] is already stopped");
                return;
            }

            try
            {
                _cancellation.Cancel();
                Task.WaitAll(new[] {_task}, 30 * 1000);
                _cancellation.Dispose();
                _task.Dispose();
            }
            catch (Exception e)
            {
                Logger.Warn($"Queue processor [{Configuration.Alias}] has not stopped gracefully - {e}");
            }
            finally
            {
                _task = null;
            }
        }

        public void ProcessMessageLoop()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                IMessage msg;

                try
                {
                    Logger.Debug($"Waiting for message, queue processor [{Configuration.Alias}]");
                    msg = _queueClient.GetMessage();
                    if (msg == null)
                    {
                        Logger.Debug($"No message received, queue processor [{Configuration.Alias}]");
                        continue;
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"Queue processor [{Configuration.Alias}], error waiting for queue message - {e}");
                    continue;
                }

                _messagesReceived++;

                Logger.Debug($"Message received, queue processor [{Configuration.Alias}]");
                if (Logger.IsTraceEnabled)
                {
                    Logger.Trace($"[{Configuration.Alias}]: {msg.Content}");
                }

                try
                {
                    Logger.Debug($"Processing message, queue processor [{Configuration.Alias}], url {Configuration.RedriveUrl}");
                    var messageProcessor = _messageProcessorFactory.CreateMessageProcessor(Configuration);
                    Logger.Debug($"Using {messageProcessor.GetType()} processor");
                    messageProcessor.ProcessMessage(msg.Content, Configuration);
                    Logger.Debug($"Processing complete, queue processor [{Configuration.Alias}]");

                    _messagesSent++;

                    try
                    {
                        Logger.Debug($"Deleting message, queue processor [{Configuration.Alias}], id [{msg.MessageIdentifier}]");
                        _queueClient.DeleteMessage(msg);
                        Logger.Debug($"Message deleted, queue processor [{Configuration.Alias}], id [{msg.MessageIdentifier}]");
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"Could not delete message [{msg.MessageIdentifier}, queue processor [{Configuration.Alias}] - MESSAGE REMAINS IN QUEUE! - {e}");
                    }
                }
                catch (Exception e)
                {
                    _messagesFailed++;

                    Logger.Error($"Error processing message [{msg.MessageIdentifier}[, queue processor [{Configuration.Alias}] - {e}");
                    Logger.Error($"Message [{msg.MessageIdentifier}[, queue processor [{Configuration.Alias}] follows \r\n{msg.Content}");
                }
                finally
                {
                    Logger.Info($"Queue processor [{Configuration.Alias}], messages received {_messagesReceived}, sent {_messagesSent}, failed {_messagesFailed}");
                }
            }
        }
    }
}