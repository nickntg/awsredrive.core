using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using AWSRedrive.Interfaces;

namespace AWSRedrive
{
    public class AwsQueueClient : IQueueClient, IDisposable
    {
        public ConfigurationEntry ConfigurationEntry { get; set; }

        private IAmazonSQS _client;

        public void Init()
        {
            var config = new AmazonSQSConfig();
            if (!string.IsNullOrEmpty(ConfigurationEntry.ServiceUrl))
            {
                config.ServiceURL = ConfigurationEntry.ServiceUrl;
            }
            else
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(ConfigurationEntry.Region);
            }

            if (string.IsNullOrEmpty(ConfigurationEntry.AccessKey) &&
                string.IsNullOrEmpty(ConfigurationEntry.SecretKey))
            {
                // AWS credentials set either in configuration or for the machine running this.
                _client = new AmazonSQSClient(config);
            }
            else
            {
                // Explicit AWS credentials.
                _client = new AmazonSQSClient(ConfigurationEntry.AccessKey,
                    ConfigurationEntry.SecretKey,
                    config);
            }
        }

        public IMessage GetMessage()
        {
            return GetMessageInternalAsync().Result;
        }

        private async Task<IMessage> GetMessageInternalAsync()
        {
            var request = new ReceiveMessageRequest
            {
                MaxNumberOfMessages = 1,
                QueueUrl = ConfigurationEntry.QueueUrl,
                WaitTimeSeconds = 20,
                MessageAttributeNames = new List<string> {"*"}
            };

            using (var source = new CancellationTokenSource(20 * 1000))
            {
                try
                {
                    var response = await _client.ReceiveMessageAsync(request, source.Token);
                    if (response?.Messages?.Count >= 1)
                    {
                        return new SqsMessage(response.Messages[0].ReceiptHandle,
                            response.Messages[0].Body,
                            (response.Messages[0].MessageAttributes)
                                .ToDictionary(item => item.Key, item => item.Value.StringValue));
                    }

                    return null;
                }
                catch (OperationCanceledException)
                {
                    if (source.Token.IsCancellationRequested)
                    {
                        return null;
                    }

                    throw;
                }
            }
        }

        public void DeleteMessage(IMessage message)
        {
            DeleteMessageInternalAsync(message);
        }

        private async void DeleteMessageInternalAsync(IMessage message)
        {
            var request = new DeleteMessageRequest
            {
                QueueUrl = ConfigurationEntry.QueueUrl,
                ReceiptHandle = message.MessageIdentifier
            };

            using (var source = new CancellationTokenSource(20 * 1000))
            {
                await _client.DeleteMessageAsync(request, source.Token);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _client?.Dispose();
            }
        }
    }
}