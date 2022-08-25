using System.Collections.Generic;
using AWSRedrive.Interfaces;
using Newtonsoft.Json;
using NLog;

namespace AWSRedrive
{
    public class ConfigurationChangeManager : IConfigurationChangeManager
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public void ReadChanges(IConfigurationReader configurationReader, 
            List<IQueueProcessor> processors,
            IQueueClientFactory queueClientFactory, 
            IMessageProcessorFactory messageProcessorFactory,
            IQueueProcessorFactory queueProcessorFactory)
        {
            var configurations = configurationReader.ReadConfiguration();

            /*
             * First, create processors to add.
            */
            var toAdd = FindConfigsToAdd(configurations, 
                processors, 
                queueClientFactory, 
                messageProcessorFactory,
                queueProcessorFactory);

            /*
             * Second, find processors to remove.
             */
            var toRemove = FindEntriesToRemove(configurations, processors);

            /*
             * Now remove those for removal and add the new ones.
             */

            foreach (var processor in toRemove)
            {
                Logger.Info($"Stopping queueprocessor for queue [{processor.Configuration.QueueUrl}], url [{processor.Configuration.RedriveUrl}], alias [{processor.Configuration.Alias}]");
                processor.Stop();
                processors.Remove(processor);
            }

            foreach (var processor in toAdd)
            {
                Logger.Info($"Starting new queueprocessor for queue [{processor.Configuration.QueueUrl}], url [{processor.Configuration.RedriveUrl}], alias [{processor.Configuration.Alias}]");
                processor.Start();
                processors.Add(processor);
            }
        }

        private List<IQueueProcessor> FindConfigsToAdd(List<ConfigurationEntry> configurations, 
            List<IQueueProcessor> processors,
            IQueueClientFactory queueClientFactory,
            IMessageProcessorFactory messageProcessorFactory,
            IQueueProcessorFactory queueProcessorFactory)
        {
            var toAdd = new List<IQueueProcessor>();
            foreach (var config in configurations)
            {
                if (!config.Active)
                {
                    continue;
                }

                var processorConfig = JsonConvert.SerializeObject(config);
                var found = false;
                foreach (var processor in processors)
                {
                    found = JsonConvert.SerializeObject(processor.Configuration) == processorConfig;

                    if (found)
                    {
                        break;
                    }
                }

                if (!found)
                {
                    Logger.Debug($"Creating new queueprocessor for queue [{config.QueueUrl}], url [{config.RedriveUrl}], alias [{config.Alias}]");
                    var queueClient = queueClientFactory.CreateClient(config);
                    queueClient.Init();
                    var queueProcessor = queueProcessorFactory.CreateQueueProcessor();
                    queueProcessor.Init(queueClient, messageProcessorFactory, config);
                    toAdd.Add(queueProcessor);
                }
            }

            return toAdd;
        }

        private List<IQueueProcessor> FindEntriesToRemove(List<ConfigurationEntry> configurations,
            List<IQueueProcessor> processors)
        {
            var toRemove = new List<IQueueProcessor>();
            foreach (var processor in processors)
            {
                var processorConfig = JsonConvert.SerializeObject(processor.Configuration);
                var found = false;
                foreach (var config in configurations)
                {
                    found = JsonConvert.SerializeObject(config) == processorConfig;
                    if (found)
                    {
                        break;
                    }
                }

                if (!found)
                {
                    toRemove.Add(processor);
                }
            }

            return toRemove;
        }
    }
}