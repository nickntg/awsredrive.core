using System;
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
            if (string.IsNullOrEmpty(ConfigurationEntry.AccessKey) &&
                string.IsNullOrEmpty(ConfigurationEntry.SecretKey))
            {
                // AWS credentials set either in configuration or for the machine running this.
                _client = new AmazonSQSClient(RegionEndpoint.GetBySystemName(ConfigurationEntry.Region));
            }
            else
            {
                // Explicit AWS credentials.
                _client = new AmazonSQSClient(ConfigurationEntry.AccessKey,
                    ConfigurationEntry.SecretKey,
                    RegionEndpoint.GetBySystemName(ConfigurationEntry.Region));
            }
        }

        public IMessage GetMessage()
        {
            return GetMessageInternal().Result;
        }

        private async Task<IMessage> GetMessageInternal()
        {
            var request = new ReceiveMessageRequest
            {
                MaxNumberOfMessages = 1,
                QueueUrl = ConfigurationEntry.QueueUrl,
                WaitTimeSeconds = 20
            };

            using (var source = new CancellationTokenSource(20 * 1000))
            {
                try
                {
                    var response = await _client.ReceiveMessageAsync(request, source.Token);
                    if (response?.Messages?.Count >= 1)
                    {
                        return new SqsMessage(response.Messages[0].ReceiptHandle,
                            response.Messages[0].Body);
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
            DeleteMessageInternal(message);
        }

        private async void DeleteMessageInternal(IMessage message)
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
            _client?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}