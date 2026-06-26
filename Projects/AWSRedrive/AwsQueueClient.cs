using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;

namespace AWSRedrive
{
    public class AwsQueueClient : IQueueClient, IDisposable
    {
        public ConfigurationEntry ConfigurationEntry { get; set; }

        protected IAmazonSQS _client;

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

            if (!string.IsNullOrEmpty(ConfigurationEntry.Profile) && 
                string.IsNullOrEmpty(ConfigurationEntry.AccessKey) &&
                string.IsNullOrEmpty(ConfigurationEntry.SecretKey))
            {
                // Configured profile.
                config.Profile = new Profile(ConfigurationEntry.Profile);
            }

            if (string.IsNullOrEmpty(ConfigurationEntry.AccessKey) &&
                string.IsNullOrEmpty(ConfigurationEntry.SecretKey))
            {
                // Configured profile or default profile.
                _client = new AmazonSQSClient(config);
            }
            else
            {
                // Explicit credentials.
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
                MessageAttributeNames = ["*"],
                MessageSystemAttributeNames = ["SentTimestamp"]
            };

            using (var source = new CancellationTokenSource(20 * 1000))
            {
                try
                {
                    var response = await _client.ReceiveMessageAsync(request, source.Token);
                    if (response?.Messages?.Count >= 1)
                    {
                        var message = response.Messages[0];
                        
                        var attributes = message.MessageAttributes?
                            .ToDictionary(item => item.Key, item => item.Value.StringValue)
                            ?? new Dictionary<string, string>();

                        if (message.Attributes != null)
                        {
                            foreach (var item in message.Attributes)
                            {
                                attributes.Add(item.Key, item.Value);
                            }
                        }

                        return new SqsMessage(message.MessageId, message.ReceiptHandle, message.Body, attributes);
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
                ReceiptHandle = message.ReceiptHandle
            };

            using (var source = new CancellationTokenSource(20 * 1000))
            {
                await _client.DeleteMessageAsync(request, source.Token);
            }
        }

        public string GetDlqUrl()
        {
            try
            {
                return GetDlqUrlAsync().Result;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> GetDlqUrlAsync()
        {
            var request = new GetQueueAttributesRequest
            {
                QueueUrl = ConfigurationEntry.QueueUrl,
                AttributeNames = new List<string> { "RedrivePolicy" }
            };

            using (var source = new CancellationTokenSource(10 * 1000))
            {
                var response = await _client.GetQueueAttributesAsync(request, source.Token);
                
                if (response.Attributes == null || 
                    !response.Attributes.TryGetValue("RedrivePolicy", out var policy) ||
                    string.IsNullOrEmpty(policy))
                {
                    return null;
                }

                // Parse RedrivePolicy JSON: {"deadLetterTargetArn":"arn:aws:sqs:region:account:queue-dlq","maxReceiveCount":3}
                var match = System.Text.RegularExpressions.Regex.Match(
                    policy, 
                    @"""deadLetterTargetArn""\s*:\s*""([^""]+)""");
                
                if (!match.Success) return null;

                var arn = match.Groups[1].Value;
                // ARN format: arn:aws:sqs:region:account:queue-name
                var parts = arn.Split(':');
                if (parts.Length < 6) return null;

                var region = parts[3];
                var account = parts[4];
                var queueName = parts[5];

                // Build queue URL
                if (!string.IsNullOrEmpty(ConfigurationEntry.ServiceUrl))
                {
                    // LocalStack or custom endpoint
                    return $"{ConfigurationEntry.ServiceUrl}/{account}/{queueName}";
                }
                
                return $"https://sqs.{region}.amazonaws.com/{account}/{queueName}";
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