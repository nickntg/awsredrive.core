using System;
using System.Collections.Generic;
using System.Threading;
using AWSRedrive;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using FakeItEasy;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class QueueManagerTests
    {
        [Fact]
        public void ProcessMessage_Successful_DeletesMessage()
        {
            var testMessage = new TestMessage("msg-123", "handle", "content");
            var callCount = 0;

            var queueClientMock = A.Fake<IQueueClient>();
            A.CallTo(() => queueClientMock.GetMessage()).ReturnsLazily(() =>
            {
                callCount++;
                if (callCount == 1) return testMessage;
                Thread.Sleep(100);
                return null;
            });
            A.CallTo(() => queueClientMock.DeleteMessage(testMessage)).DoesNothing();

            var messageProcessorMock = A.Fake<IMessageProcessor>();
            A.CallTo(() => messageProcessorMock.ProcessMessage(
                A<string>.Ignored, 
                A<Dictionary<string, string>>.Ignored,
                A<ConfigurationEntry>.Ignored, 
                A<EntryLogger>.Ignored)).DoesNothing();

            var processorFactoryMock = A.Fake<IMessageProcessorFactory>();
            A.CallTo(() => processorFactoryMock.CreateMessageProcessor(A<ConfigurationEntry>.Ignored))
                .Returns(messageProcessorMock);

            var config = new ConfigurationEntry 
            { 
                Alias = "test-delete-" + Guid.NewGuid(), 
                QueueUrl = "http://test", 
                LogLevel = "Error" 
            };
            
            var processor = new QueueProcessor();
            processor.Init(queueClientMock, processorFactoryMock, config);
            processor.Start();

            Thread.Sleep(1000);

            processor.Stop();

            A.CallTo(() => messageProcessorMock.ProcessMessage(
                A<string>.Ignored, 
                A<Dictionary<string, string>>.Ignored,
                A<ConfigurationEntry>.Ignored, 
                A<EntryLogger>.Ignored)).MustHaveHappenedOnceExactly();
            A.CallTo(() => queueClientMock.DeleteMessage(testMessage)).MustHaveHappenedOnceExactly();

            MetricsStore.Remove(config.Alias);
        }

        [Fact]
        public void ProcessMessage_Failed_DoesNotDeleteMessage()
        {
            var testMessage = new TestMessage("msg-123", "handle", "content");
            var callCount = 0;

            var queueClientMock = A.Fake<IQueueClient>();
            A.CallTo(() => queueClientMock.GetMessage()).ReturnsLazily(() =>
            {
                callCount++;
                if (callCount == 1) return testMessage;
                Thread.Sleep(100);
                return null;
            });

            var messageProcessorMock = A.Fake<IMessageProcessor>();
            A.CallTo(() => messageProcessorMock.ProcessMessage(
                A<string>.Ignored, 
                A<Dictionary<string, string>>.Ignored,
                A<ConfigurationEntry>.Ignored, 
                A<EntryLogger>.Ignored)).Throws<Exception>();

            var processorFactoryMock = A.Fake<IMessageProcessorFactory>();
            A.CallTo(() => processorFactoryMock.CreateMessageProcessor(A<ConfigurationEntry>.Ignored))
                .Returns(messageProcessorMock);

            var config = new ConfigurationEntry 
            { 
                Alias = "test-no-delete-" + Guid.NewGuid(), 
                QueueUrl = "http://test", 
                LogLevel = "Error" 
            };
            
            var processor = new QueueProcessor();
            processor.Init(queueClientMock, processorFactoryMock, config);
            processor.Start();

            Thread.Sleep(1000);

            processor.Stop();

            A.CallTo(() => queueClientMock.DeleteMessage(A<IMessage>.Ignored)).MustNotHaveHappened();

            MetricsStore.Remove(config.Alias);
        }

        [Fact]
        public void ProcessMessage_UpdatesMetrics()
        {
            var testMessage = new TestMessage("msg-123", "handle", "content");
            var callCount = 0;

            var queueClientMock = A.Fake<IQueueClient>();
            A.CallTo(() => queueClientMock.GetMessage()).ReturnsLazily(() =>
            {
                callCount++;
                if (callCount == 1) return testMessage;
                Thread.Sleep(100);
                return null;
            });

            var messageProcessorMock = A.Fake<IMessageProcessor>();
            A.CallTo(() => messageProcessorMock.ProcessMessage(
                A<string>.Ignored, 
                A<Dictionary<string, string>>.Ignored,
                A<ConfigurationEntry>.Ignored, 
                A<EntryLogger>.Ignored)).DoesNothing();

            var processorFactoryMock = A.Fake<IMessageProcessorFactory>();
            A.CallTo(() => processorFactoryMock.CreateMessageProcessor(A<ConfigurationEntry>.Ignored))
                .Returns(messageProcessorMock);

            var alias = "metrics-test-" + Guid.NewGuid();
            var config = new ConfigurationEntry 
            { 
                Alias = alias, 
                QueueUrl = "http://test", 
                LogLevel = "Error" 
            };
            
            var processor = new QueueProcessor();
            processor.Init(queueClientMock, processorFactoryMock, config);
            
            var metrics = MetricsStore.GetOrCreate(alias);
            Assert.Equal(0, metrics.MessagesReceived);

            processor.Start();

            Thread.Sleep(1000);

            processor.Stop();

            Assert.Equal(1, metrics.MessagesReceived);
            Assert.Equal(1, metrics.MessagesSent);
            Assert.Equal(0, metrics.MessagesFailed);

            MetricsStore.Remove(alias);
        }

        [Fact]
        public void ProcessMessage_Failed_UpdatesFailedMetrics()
        {
            var testMessage = new TestMessage("msg-123", "handle", "content");
            var callCount = 0;

            var queueClientMock = A.Fake<IQueueClient>();
            A.CallTo(() => queueClientMock.GetMessage()).ReturnsLazily(() =>
            {
                callCount++;
                if (callCount == 1) return testMessage;
                Thread.Sleep(100);
                return null;
            });

            var messageProcessorMock = A.Fake<IMessageProcessor>();
            A.CallTo(() => messageProcessorMock.ProcessMessage(
                A<string>.Ignored, 
                A<Dictionary<string, string>>.Ignored,
                A<ConfigurationEntry>.Ignored, 
                A<EntryLogger>.Ignored)).Throws<Exception>();

            var processorFactoryMock = A.Fake<IMessageProcessorFactory>();
            A.CallTo(() => processorFactoryMock.CreateMessageProcessor(A<ConfigurationEntry>.Ignored))
                .Returns(messageProcessorMock);

            var alias = "failed-test-" + Guid.NewGuid();
            var config = new ConfigurationEntry 
            { 
                Alias = alias, 
                QueueUrl = "http://test", 
                LogLevel = "Error" 
            };
            
            var processor = new QueueProcessor();
            processor.Init(queueClientMock, processorFactoryMock, config);

            var metrics = MetricsStore.GetOrCreate(alias);

            processor.Start();

            Thread.Sleep(1000);

            processor.Stop();

            Assert.Equal(1, metrics.MessagesReceived);
            Assert.Equal(0, metrics.MessagesSent);
            Assert.Equal(1, metrics.MessagesFailed);

            MetricsStore.Remove(alias);
        }

        private class TestMessage : IMessage
        {
            public TestMessage(string messageId, string receiptHandle, string content)
            {
                MessageId = messageId;
                ReceiptHandle = receiptHandle;
                Content = content;
                Attributes = new Dictionary<string, string>();
            }

            public string MessageId { get; }
            public string ReceiptHandle { get; }
            public string Content { get; }
            public Dictionary<string, string> Attributes { get; }
        }
    }
}