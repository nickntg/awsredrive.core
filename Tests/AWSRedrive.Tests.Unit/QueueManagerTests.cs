using System.Collections.Generic;
using System.Threading;
using AWSRedrive.Interfaces;
using FakeItEasy;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class QueueManagerTests
    {
        [Fact]
        public void DoesNothingWithoutQueuedMessage()
        {
            var configuration = new ConfigurationEntry { Alias = "#1", RedriveUrl = "http://here.com/", Active = true };
            var queueClientMock = A.Fake<IQueueClient>(x => x.Strict());
            A.CallTo(() => queueClientMock.GetMessage())
                .Invokes(() =>
                {
                    Thread.Sleep(2000);
                })
                .Returns(null);

            var processor = new QueueProcessor();
            processor.Init(queueClientMock, null, configuration);
            processor.Start();
            Thread.Sleep(1000);
            processor.Stop();

            A.CallTo(() => queueClientMock.GetMessage())
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public void ReceivesAndSendsMessage()
        {
            var configuration = new ConfigurationEntry { Alias = "#1", RedriveUrl = "http://here.com/", Active = true };
            var queueClientMock = A.Fake<IQueueClient>(x => x.Strict());
            A.CallTo(() => queueClientMock.GetMessage())
                .Returns(new SqsMessage("id", "content", new Dictionary<string, string>()));
            A.CallTo(() => queueClientMock.DeleteMessage(A<IMessage>.Ignored))
                .Invokes((IMessage _) =>
                {
                    Thread.Sleep(2000);
                })
                .DoesNothing();

            var messageProcessorMock = A.Fake<IMessageProcessor>(x => x.Strict());
            A.CallTo(() => messageProcessorMock.ProcessMessage(A<string>.Ignored, A<Dictionary<string, string>>.Ignored,
                    A<ConfigurationEntry>.Ignored))
                .DoesNothing();
            
            var processorFactoryMock = A.Fake<IMessageProcessorFactory>(x => x.Strict());
            A.CallTo(() => processorFactoryMock.CreateMessageProcessor(A<ConfigurationEntry>.Ignored))
                .Returns(messageProcessorMock);

            var processor = new QueueProcessor();
            processor.Init(queueClientMock, processorFactoryMock, configuration);
            processor.Start();
            Thread.Sleep(1000);
            processor.Stop();

            A.CallTo(() => queueClientMock.GetMessage())
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => queueClientMock.DeleteMessage(A<IMessage>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => messageProcessorMock.ProcessMessage(A<string>.Ignored, A<Dictionary<string, string>>.Ignored,
                A<ConfigurationEntry>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => processorFactoryMock.CreateMessageProcessor(A<ConfigurationEntry>.Ignored))
                .MustHaveHappenedOnceExactly();
        }
    }
}
