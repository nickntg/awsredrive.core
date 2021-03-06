﻿using System.Collections.Generic;
using AWSRedrive.Interfaces;
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

                var found = false;
                foreach (var processor in processors)
                {
                    found = ((config.Alias == processor.Configuration.Alias) &&
                             (config.AccessKey == processor.Configuration.AccessKey) &&
                             (config.AwsGatewayToken == processor.Configuration.AwsGatewayToken) &&
                             (config.AuthToken == processor.Configuration.AuthToken) &&
                             (config.QueueUrl == processor.Configuration.QueueUrl) &&
                             (config.RedriveUrl == processor.Configuration.RedriveUrl) &&
                             (config.Region == processor.Configuration.Region) &&
                             (config.SecretKey == processor.Configuration.SecretKey));
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
                var found = false;
                foreach (var config in configurations)
                {
                    found = ((config.Alias == processor.Configuration.Alias) &&
                             (config.AccessKey == processor.Configuration.AccessKey) &&
                             (config.AwsGatewayToken == processor.Configuration.AwsGatewayToken) &&
                             (config.AuthToken == processor.Configuration.AuthToken) &&
                             (config.QueueUrl == processor.Configuration.QueueUrl) &&
                             (config.RedriveUrl == processor.Configuration.RedriveUrl) &&
                             (config.Region == processor.Configuration.Region) &&
                             (config.SecretKey == processor.Configuration.SecretKey) &&
                             (config.Active));
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