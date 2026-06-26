using System;
using System.Collections.Generic;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
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

            var toAdd = FindConfigsToAdd(configurations, 
                processors, 
                queueClientFactory, 
                messageProcessorFactory,
                queueProcessorFactory);

            var toRemove = FindEntriesToRemove(configurations, processors);

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

        /// <summary>
        /// Compares configurations excluding LogLevel (can be changed at runtime via dashboard).
        /// Changes to LogLevel persist until app restart.
        /// </summary>
        private bool ConfigurationsMatch(ConfigurationEntry a, ConfigurationEntry b)
        {
            if (a == null || b == null) return false;
            
            return a.Alias == b.Alias &&
                   a.Active == b.Active &&
                   a.Profile == b.Profile &&
                   a.AccessKey == b.AccessKey &&
                   a.SecretKey == b.SecretKey &&
                   a.QueueUrl == b.QueueUrl &&
                   a.Region == b.Region &&
                   a.RedriveUrl == b.RedriveUrl &&
                   a.RedriveScript == b.RedriveScript &&
                   a.RedriveKafkaTopic == b.RedriveKafkaTopic &&
                   a.KafkaBootstrapServers == b.KafkaBootstrapServers &&
                   a.KafkaClientId == b.KafkaClientId &&
                   a.UseKafkaCompression == b.UseKafkaCompression &&
                   a.AwsGatewayToken == b.AwsGatewayToken &&
                   a.AuthToken == b.AuthToken &&
                   a.BasicAuthUserName == b.BasicAuthUserName &&
                   a.BasicAuthPassword == b.BasicAuthPassword &&
                   a.UsePUT == b.UsePUT &&
                   a.UseGET == b.UseGET &&
                   a.UseDelete == b.UseDelete &&
                   a.Timeout == b.Timeout &&
                   a.IgnoreCertificateErrors == b.IgnoreCertificateErrors &&
                   a.UnpackAttributesAsHeaders == b.UnpackAttributesAsHeaders &&
                   a.ServiceUrl == b.ServiceUrl;
            // LogLevel intentionally excluded - runtime changes persist until restart
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
                    found = ConfigurationsMatch(processor.Configuration, config);
                    if (found) break;
                }

                if (!found)
                {
                    Logger.Debug($"Creating new queueprocessor for queue [{config.QueueUrl}], url [{config.RedriveUrl}], alias [{config.Alias}]");
                    var queueClient = queueClientFactory.CreateClient(config);
                    queueClient.Init();
                    
                    // Fetch DLQ URL from SQS RedrivePolicy
                    try
                    {
                        config.DlqUrl = queueClient.GetDlqUrl();
                        if (!string.IsNullOrEmpty(config.DlqUrl))
                        {
                            Logger.Debug($"DLQ for [{config.Alias}]: {config.DlqUrl}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Could not fetch DLQ for [{config.Alias}]: {ex.Message}");
                    }
                    
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
                    found = ConfigurationsMatch(config, processor.Configuration);
                    if (found) break;
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