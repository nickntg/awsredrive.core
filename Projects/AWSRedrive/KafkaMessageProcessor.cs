using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            var topic = configurationEntry.RedriveKafkaTopic;
            var timeout = configurationEntry.Timeout ?? 1000;
            
            logger.Debug($"Preparing Kafka produce to topic {topic}");
            
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
                logger.Trace("Compression: Snappy");
            }

            logger.Trace($"BootstrapServers: {configurationEntry.KafkaBootstrapServers}, ClientId: {config.ClientId}, Timeout: {timeout}ms");
            logger.Trace($"Message size: {message?.Length ?? 0} chars");

            using (var producer = new ProducerBuilder<Null, string>(config).Build())
            {
                var ct = new CancellationTokenSource();
                ct.CancelAfter(timeout);
                
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    var result = producer.ProduceAsync(topic,
                        new Message<Null, string> { Value = message }, ct.Token).Result;
                    stopwatch.Stop();
                    
                    if (result.Status == PersistenceStatus.Persisted)
                    {
                        logger.Debug($"Kafka produce successful ({stopwatch.ElapsedMilliseconds}ms) - Partition: {result.Partition.Value}, Offset: {result.Offset.Value}");
                        logger.Trace($"Topic: {result.Topic}, Timestamp: {result.Timestamp.UtcDateTime:O}");
                    }
                    else
                    {
                        logger.Debug($"Kafka produce failed ({stopwatch.ElapsedMilliseconds}ms) - Status: {result.Status}");
                        throw new InvalidOperationException(
                            $"Kafka produce to topic {topic} failed, status={result.Status}");
                    }
                }
                catch (OperationCanceledException)
                {
                    stopwatch.Stop();
                    logger.Debug($"Kafka produce timeout after {stopwatch.ElapsedMilliseconds}ms");
                    throw new TimeoutException($"Kafka produce to topic {topic} timed out after {timeout}ms");
                }
                catch (ProduceException<Null, string> ex)
                {
                    stopwatch.Stop();
                    logger.Debug($"Kafka produce error ({stopwatch.ElapsedMilliseconds}ms) - {ex.Error.Code}: {ex.Error.Reason}");
                    throw;
                }
            }
        }
    }
}