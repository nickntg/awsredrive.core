using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AWSRedrive.Interfaces;
using NLog;

namespace AWSRedrive
{
    public class Orchestrator : IOrchestrator
    {
        public bool IsProcessing { get; private set; }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IConfigurationReader _configurationReader;
        private readonly IQueueClientFactory _queueClientFactory;
        private readonly IMessageProcessorFactory _messageProcessorFactory;
        private readonly IQueueProcessorFactory _queueProcessorFactory;
        private readonly IConfigurationChangeManager _configurationChangeManager;
        private Task _task;
        private CancellationTokenSource _cancellation;
        private List<IQueueProcessor> _processors;

        public Orchestrator(IConfigurationReader configurationReader,
            IQueueClientFactory queueClientFactory,
            IMessageProcessorFactory messageProcessorFactory,
            IQueueProcessorFactory queueProcessorFactory,
            IConfigurationChangeManager configurationChangeManager)
        {
            IsProcessing = false;
            _configurationReader = configurationReader;
            _queueClientFactory = queueClientFactory;
            _messageProcessorFactory = messageProcessorFactory;
            _queueProcessorFactory = queueProcessorFactory;
            _configurationChangeManager = configurationChangeManager;
        }

        public void Start()
        {
            _processors = new List<IQueueProcessor>();
            _cancellation = new CancellationTokenSource();
            _task = new Task(StartProcessing, _cancellation.Token);
            _task.Start();
            IsProcessing = true;
        }

        public void StartProcessing()
        {
            var lastDateTimeChecked = DateTime.Now.AddYears(-1);

            while (!_cancellation.IsCancellationRequested)
            {
                if (DateTime.Now.Subtract(lastDateTimeChecked).TotalSeconds > 60)
                {
                    try
                    {
                        _configurationChangeManager.ReadChanges(_configurationReader, _processors, _queueClientFactory, _messageProcessorFactory, _queueProcessorFactory);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                    
                    lastDateTimeChecked = DateTime.Now;
                }

                Thread.Sleep(1000);
            }

            foreach (var processor in _processors)
            {
                processor.Stop();
            }

            Thread.Sleep(5000);
        }

        public void Stop()
        {
            _cancellation.Cancel();
            while (!_task.IsCompleted)
            {
                Thread.Sleep(100);
            }
            _cancellation.Dispose();
            _task.Dispose();
            IsProcessing = false;
        }
    }
}
