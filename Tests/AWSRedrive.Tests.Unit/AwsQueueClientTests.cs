using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using AWSRedrive.Models;
using FakeItEasy;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class AwsQueueClientTests
    {
        [Fact]
        public void GetMessage_WithNullMessageAttributes_ReturnsMessageWithEmptyAttributes()
        {
            // Arrange
            var mockSqsClient = A.Fake<IAmazonSQS>();
            var response = new ReceiveMessageResponse
            {
                Messages =
                [
                    new()
                    {
                        MessageId = "test-message-id",
                        ReceiptHandle = "test-handle",
                        Body = "test-body",
                        MessageAttributes = null, // SDK 4.x can return null
                        Attributes = null // SDK 4.x can return null
                    }
                ]
            };

            A.CallTo(() => mockSqsClient.ReceiveMessageAsync(
                A<ReceiveMessageRequest>.Ignored,
                A<CancellationToken>.Ignored))
                .Returns(Task.FromResult(response));

            var client = new TestableAwsQueueClient(mockSqsClient)
            {
                ConfigurationEntry = new ConfigurationEntry
                {
                    QueueUrl = "https://sqs.test.amazonaws.com/test-queue",
                    Region = "eu-central-1"
                }
            };

            // Act
            var message = client.GetMessage();

            // Assert
            Assert.NotNull(message);
            Assert.Equal("test-message-id", message.MessageId);
            Assert.Equal("test-handle", message.ReceiptHandle);
            Assert.Equal("test-body", message.Content);
            Assert.NotNull(message.Attributes);
            Assert.Empty(message.Attributes);
        }

        [Fact]
        public void GetMessage_WithEmptyMessageAttributes_ReturnsMessageWithEmptyAttributes()
        {
            // Arrange
            var mockSqsClient = A.Fake<IAmazonSQS>();
            var response = new ReceiveMessageResponse
            {
                Messages =
                [
                    new()
                    {
                        MessageId = "test-message-id",
                        ReceiptHandle = "test-handle",
                        Body = "test-body",
                        MessageAttributes = new Dictionary<string, MessageAttributeValue>(),
                        Attributes = new Dictionary<string, string>()
                    }
                ]
            };

            A.CallTo(() => mockSqsClient.ReceiveMessageAsync(
                A<ReceiveMessageRequest>.Ignored,
                A<CancellationToken>.Ignored))
                .Returns(Task.FromResult(response));

            var client = new TestableAwsQueueClient(mockSqsClient)
            {
                ConfigurationEntry = new ConfigurationEntry
                {
                    QueueUrl = "https://sqs.test.amazonaws.com/test-queue",
                    Region = "eu-central-1"
                }
            };

            // Act
            var message = client.GetMessage();

            // Assert
            Assert.NotNull(message);
            Assert.NotNull(message.Attributes);
            Assert.Empty(message.Attributes);
        }

        [Fact]
        public void GetMessage_WithMessageAttributes_ReturnsMessageWithAttributes()
        {
            // Arrange
            var mockSqsClient = A.Fake<IAmazonSQS>();
            var response = new ReceiveMessageResponse
            {
                Messages =
                [
                    new()
                    {
                        MessageId = "test-message-id",
                        ReceiptHandle = "test-handle",
                        Body = "test-body",
                        MessageAttributes = new Dictionary<string, MessageAttributeValue>
                        {
                            { "customAttr", new MessageAttributeValue { StringValue = "customValue" } }
                        },
                        Attributes = new Dictionary<string, string>
                        {
                            { "SentTimestamp", "1234567890" }
                        }
                    }
                ]
            };

            A.CallTo(() => mockSqsClient.ReceiveMessageAsync(
                A<ReceiveMessageRequest>.Ignored,
                A<CancellationToken>.Ignored))
                .Returns(Task.FromResult(response));

            var client = new TestableAwsQueueClient(mockSqsClient)
            {
                ConfigurationEntry = new ConfigurationEntry
                {
                    QueueUrl = "https://sqs.test.amazonaws.com/test-queue",
                    Region = "eu-central-1"
                }
            };

            // Act
            var message = client.GetMessage();

            // Assert
            Assert.NotNull(message);
            Assert.Equal("test-message-id", message.MessageId);
            Assert.Equal(2, message.Attributes.Count);
            Assert.Equal("customValue", message.Attributes["customAttr"]);
            Assert.Equal("1234567890", message.Attributes["SentTimestamp"]);
        }

        [Fact]
        public void GetMessage_WithNullMessageAttributesAndValidAttributes_ReturnsMergedAttributes()
        {
            // Arrange
            var mockSqsClient = A.Fake<IAmazonSQS>();
            var response = new ReceiveMessageResponse
            {
                Messages =
                [
                    new()
                    {
                        MessageId = "test-message-id",
                        ReceiptHandle = "test-handle",
                        Body = "test-body",
                        MessageAttributes = null, // SDK 4.x can return null
                        Attributes = new Dictionary<string, string>
                        {
                            { "SentTimestamp", "1234567890" }
                        }
                    }
                ]
            };

            A.CallTo(() => mockSqsClient.ReceiveMessageAsync(
                A<ReceiveMessageRequest>.Ignored,
                A<CancellationToken>.Ignored))
                .Returns(Task.FromResult(response));

            var client = new TestableAwsQueueClient(mockSqsClient)
            {
                ConfigurationEntry = new ConfigurationEntry
                {
                    QueueUrl = "https://sqs.test.amazonaws.com/test-queue",
                    Region = "eu-central-1"
                }
            };

            // Act
            var message = client.GetMessage();

            // Assert
            Assert.NotNull(message);
            Assert.Single(message.Attributes);
            Assert.Equal("1234567890", message.Attributes["SentTimestamp"]);
        }

        [Fact]
        public void GetMessage_WithNoMessages_ReturnsNull()
        {
            // Arrange
            var mockSqsClient = A.Fake<IAmazonSQS>();
            var response = new ReceiveMessageResponse
            {
                Messages = new List<Message>()
            };

            A.CallTo(() => mockSqsClient.ReceiveMessageAsync(
                A<ReceiveMessageRequest>.Ignored,
                A<CancellationToken>.Ignored))
                .Returns(Task.FromResult(response));

            var client = new TestableAwsQueueClient(mockSqsClient)
            {
                ConfigurationEntry = new ConfigurationEntry
                {
                    QueueUrl = "https://sqs.test.amazonaws.com/test-queue",
                    Region = "eu-central-1"
                }
            };

            // Act
            var message = client.GetMessage();

            // Assert
            Assert.Null(message);
        }

        [Fact]
        public void DeleteMessage_WhenSqsCallFails_ThrowsToCaller()
        {
            // Arrange
            var mockSqsClient = A.Fake<IAmazonSQS>();

            A.CallTo(() => mockSqsClient.DeleteMessageAsync(
                A<DeleteMessageRequest>.Ignored,
                A<CancellationToken>.Ignored))
                .Returns(Task.FromException<DeleteMessageResponse>(
                    new AmazonSQSException("throttled")));

            var client = new TestableAwsQueueClient(mockSqsClient)
            {
                ConfigurationEntry = new ConfigurationEntry
                {
                    QueueUrl = "https://sqs.test.amazonaws.com/test-queue",
                    Region = "eu-central-1"
                }
            };

            var message = new SqsMessage("test-message-id", "test-handle", "test-body", null);

            var ex = Assert.Throws<AmazonSQSException>(() => client.DeleteMessage(message));
            Assert.Equal("throttled", ex.Message);
        }

        [Fact]
        public void DeleteMessage_WhenCancellationTimeoutElapses_ThrowsToCaller()
        {
            // Arrange
            var mockSqsClient = A.Fake<IAmazonSQS>();

            A.CallTo(() => mockSqsClient.DeleteMessageAsync(
                A<DeleteMessageRequest>.Ignored,
                A<CancellationToken>.Ignored))
                .Returns(Task.FromException<DeleteMessageResponse>(
                    new TaskCanceledException()));

            var client = new TestableAwsQueueClient(mockSqsClient)
            {
                ConfigurationEntry = new ConfigurationEntry
                {
                    QueueUrl = "https://sqs.test.amazonaws.com/test-queue",
                    Region = "eu-central-1"
                }
            };

            var message = new SqsMessage("test-message-id", "test-handle", "test-body", null);

            // Act / Assert
            Assert.Throws<TaskCanceledException>(() => client.DeleteMessage(message));
        }

        [Fact]
        public void DeleteMessage_DoesNotReturnBeforeDeleteCompletes()
        {
            // Arrange
            var mockSqsClient = A.Fake<IAmazonSQS>();
            var deleteCompleted = false;

            A.CallTo(() => mockSqsClient.DeleteMessageAsync(
                A<DeleteMessageRequest>.Ignored,
                A<CancellationToken>.Ignored))
                .ReturnsLazily(CompleteAfterDelay);

            async Task<DeleteMessageResponse> CompleteAfterDelay()
            {
                await Task.Delay(200);
                deleteCompleted = true;
                return new DeleteMessageResponse();
            }

            var client = new TestableAwsQueueClient(mockSqsClient)
            {
                ConfigurationEntry = new ConfigurationEntry
                {
                    QueueUrl = "https://sqs.test.amazonaws.com/test-queue",
                    Region = "eu-central-1"
                }
            };

            var message = new SqsMessage("test-message-id", "test-handle", "test-body", null);

            // Act
            client.DeleteMessage(message);

            // Assert
            Assert.True(deleteCompleted, "DeleteMessage returned before the SQS delete completed");
        }

        [Fact]
        public void DeleteMessage_OnSuccess_SendsQueueUrlAndReceiptHandle()
        {
            // Arrange
            var mockSqsClient = A.Fake<IAmazonSQS>();
            DeleteMessageRequest capturedRequest = null;

            A.CallTo(() => mockSqsClient.DeleteMessageAsync(
                A<DeleteMessageRequest>.Ignored,
                A<CancellationToken>.Ignored))
                .Invokes((DeleteMessageRequest request, CancellationToken _) => capturedRequest = request)
                .Returns(Task.FromResult(new DeleteMessageResponse()));

            var client = new TestableAwsQueueClient(mockSqsClient)
            {
                ConfigurationEntry = new ConfigurationEntry
                {
                    QueueUrl = "https://sqs.test.amazonaws.com/test-queue",
                    Region = "eu-central-1"
                }
            };

            var message = new SqsMessage("test-message-id", "test-handle", "test-body", null);

            // Act
            client.DeleteMessage(message);

            // Assert
            A.CallTo(() => mockSqsClient.DeleteMessageAsync(
                A<DeleteMessageRequest>.Ignored,
                A<CancellationToken>.Ignored))
                .MustHaveHappenedOnceExactly();
            Assert.NotNull(capturedRequest);
            Assert.Equal("https://sqs.test.amazonaws.com/test-queue", capturedRequest.QueueUrl);
            Assert.Equal("test-handle", capturedRequest.ReceiptHandle);
        }
    }


    /// <summary>
    /// Testable version of AwsQueueClient that allows injecting a mock IAmazonSQS
    /// </summary>
    public class TestableAwsQueueClient : AwsQueueClient
    {
        public TestableAwsQueueClient(IAmazonSQS mockClient)
        {
            Client = mockClient;
        }
    }
}