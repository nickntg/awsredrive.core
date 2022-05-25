using System;
using System.Collections.Generic;
using System.Threading;
using AWSRedrive.Interfaces;
using Confluent.Kafka;
using NLog;

namespace AWSRedrive
{
    public class KafkaMessageProcessor : IMessageProcessor
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public void ProcessMessage(string message, Dictionary<string, string> attributes, ConfigurationEntry configurationEntry)
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

            Logger.Trace($"Creating producer for kafka topic {configurationEntry.RedriveKafkaTopic}");
            using (var producer = new ProducerBuilder<Null, string>(config).Build())
            {
                var ct = new CancellationTokenSource();
                ct.CancelAfter(configurationEntry.Timeout ?? 1000);
                try
                {
                    Logger.Trace($"Posting to kafka topic {configurationEntry.RedriveKafkaTopic}");
                    var result = producer.ProduceAsync(configurationEntry.RedriveKafkaTopic,
                        new Message<Null, string> { Value = message }, ct.Token).Result;
                    if (result.Status == PersistenceStatus.Persisted)
                    {
                        Logger.Trace($"Post to kafka topic {configurationEntry.RedriveKafkaTopic} successful");
                    }
                    else
                    {
                        Logger.Trace($"Post to kafka topic {configurationEntry.RedriveKafkaTopic} failed, status={result.Status}");
                        throw new InvalidOperationException(
                            $"Post to kafka topic {configurationEntry.RedriveKafkaTopic} failed, status={result.Status}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Error while posting to kafka topic {configurationEntry.RedriveKafkaTopic}");
                    throw;
                }
            }
        }
    }
}
