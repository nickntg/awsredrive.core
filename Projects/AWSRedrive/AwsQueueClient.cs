﻿using System;
using System.Threading;
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using AWSRedrive.Interfaces;

namespace AWSRedrive
{
    public class AwsQueueClient : IQueueClient, IDisposable
    {
        public ConfigurationEntry ConfigurationEntry { get; set; }

        private readonly IAmazonSQS _client;

        public AwsQueueClient()
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
            var request = new ReceiveMessageRequest
            {
                MaxNumberOfMessages = 1,
                QueueUrl = ConfigurationEntry.QueueUrl,
                WaitTimeSeconds = 20
            };

            using (var source = new CancellationTokenSource())
            {
                var response = _client.ReceiveMessageAsync(request, source.Token);
                source.Token.WaitHandle.WaitOne();

                if (response?.Result?.Messages?.Count >= 1)
                {
                    return new SqsMessage(response.Result.Messages[0].ReceiptHandle,
                        response.Result.Messages[0].Body);
                }

                return null;
            }
        }

        public void DeleteMessage(IMessage message)
        {
            var request = new DeleteMessageRequest
            {
                QueueUrl = ConfigurationEntry.QueueUrl,
                ReceiptHandle = message.MessageIdentifier
            };

            using (var source = new CancellationTokenSource())
            {
                _client.DeleteMessageAsync(request, source.Token);
                source.Token.WaitHandle.WaitOne();
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}