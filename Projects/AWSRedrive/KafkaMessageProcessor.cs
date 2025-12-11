using System;
using System.Collections.Generic;
using System.Threading;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using Confluent.Kafka;

namespace AWSRedrive
{
    public class KafkaMessageProcessor : IMessageProcessor
    {
        public void ProcessMessage(string message, Dictionary<string, string> attributes, ConfigurationEntry configurationEntry, EntryLogger logger)
        {
            var config = new ProducerConfig
            {
                BootstrapServers = configurationEntry.KafkaBootstrapServers,
                ClientId = string.IsNullOrEmpty(configurationEntry.KafkaClientId)
                    ? "redrive"
                    : configurationEntry.KafkaClientId,
                Acks = Acks.All
            };

            if (configurationEntry.UseKafkaCompression)
            {
                config.CompressionType = CompressionType.Snappy;
            }

            logger.Trace($"Creating producer for kafka topic {configurationEntry.RedriveKafkaTopic}");
            using (var producer = new ProducerBuilder<Null, string>(config).Build())
            {
                var ct = new CancellationTokenSource();
                ct.CancelAfter(configurationEntry.Timeout ?? 1000);
                try
                {
                    logger.Trace($"Posting to kafka topic {configurationEntry.RedriveKafkaTopic}");
                    var result = producer.ProduceAsync(configurationEntry.RedriveKafkaTopic,
                        new Message<Null, string> { Value = message }, ct.Token).Result;
                    if (result.Status == PersistenceStatus.Persisted)
                    {
                        logger.Trace($"Post to kafka topic {configurationEntry.RedriveKafkaTopic} successful");
                    }
                    else
                    {
                        logger.Trace($"Post to kafka topic {configurationEntry.RedriveKafkaTopic} failed, status={result.Status}");
                        throw new InvalidOperationException(
                            $"Post to kafka topic {configurationEntry.RedriveKafkaTopic} failed, status={result.Status}");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Error while posting to kafka topic {configurationEntry.RedriveKafkaTopic}");
                    throw;
                }
            }
        }
    }
}
