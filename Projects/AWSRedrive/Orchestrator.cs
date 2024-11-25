using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AWSRedrive.DI;
using AWSRedrive.Interfaces;
using NLog;

namespace AWSRedrive
{
    public class Orchestrator(
        Injector.ConfigurationReaderResolver configurationReaderResolver,
        IConfigurationWriter configurationWriter,
        IQueueClientFactory queueClientFactory,
        IMessageProcessorFactory messageProcessorFactory,
        IQueueProcessorFactory queueProcessorFactory,
        IConfigurationChangeManager configurationChangeManager)
        : IOrchestrator
    {
        public bool IsProcessing { get; private set; }

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private IConfigurationReader _configurationReader;
        private Task _task;
        private CancellationTokenSource _cancellation;
        private List<IQueueProcessor> _processors;

        public void Start()
        {
            DetermineConfigurationLocation();
            
            _processors = [];
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
                        configurationChangeManager.ReadChanges(_configurationReader, _processors, queueClientFactory, messageProcessorFactory, queueProcessorFactory);
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

        private void DetermineConfigurationLocation()
        {
            var local = configurationReaderResolver(Constants.LocalConfigurationReader);
            var dynamo = configurationReaderResolver(Constants.DynamoConfigurationReader);

            if (local.CanBeUsed() && dynamo.CanBeUsed())
            {
                MigrateFromLocalToDynamo(local);

                _configurationReader = dynamo;
            }
            else if (local.CanBeUsed())
            {
                _configurationReader = local;
            }
            else if (dynamo.CanBeUsed())
            {
                _configurationReader = dynamo;
            }
            else
            {
                throw new InvalidOperationException("Neither local nor dynamo configuration usable");
            }
        }

        private void MigrateFromLocalToDynamo(IConfigurationReader local)
        {
            var configs = local.ReadConfiguration();

            configurationWriter.Save(configs);
        }
    }
}
