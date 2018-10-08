using System.Threading;
using AWSRedrive.Interfaces;
using Moq;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class QueueManagerTests
    {
        [Fact]
        public void DoesNothingWithoutQueuedMessage()
        {
            var configuration = new ConfigurationEntry {Alias = "#1", RedriveUrl = "http://here.com/", Active = true};
            var queueClientMock = new Mock<IQueueClient>(MockBehavior.Strict);
            queueClientMock.Setup(x => x.GetMessage()).Callback(() => Thread.Sleep(2000)).Returns((SqsMessage) null).Verifiable();

            var processor = new QueueProcessor();
            processor.Init(queueClientMock.Object, null, configuration);
            processor.Start();
            Thread.Sleep(1000);
            processor.Stop();

            queueClientMock.Verify(x => x.GetMessage(), Times.Exactly(1));
        }

        [Fact]
        public void ReceivesAndSendsMessage()
        {
            var configuration = new ConfigurationEntry { Alias = "#1", RedriveUrl = "http://here.com/", Active = true };
            var queueClientMock = new Mock<IQueueClient>(MockBehavior.Strict);
            queueClientMock.Setup(x => x.GetMessage()).Returns(new SqsMessage("id", "content")).Verifiable();
            var messageProcessorMock = new Mock<IMessageProcessor>(MockBehavior.Strict);
            messageProcessorMock.Setup(x => x.ProcessMessage(It.IsAny<string>(), It.IsAny<ConfigurationEntry>())).Verifiable();
            queueClientMock.Setup(x => x.DeleteMessage(It.IsAny<IMessage>())).Callback(() => Thread.Sleep(2000));
            var processorFactoryMock = new Mock<IMessageProcessorFactory>(MockBehavior.Strict);
            processorFactoryMock.Setup(x => x.CreateMessageProcessor(It.IsAny<ConfigurationEntry>())).Returns(messageProcessorMock.Object).Verifiable();

            var processor = new QueueProcessor();
            processor.Init(queueClientMock.Object, processorFactoryMock.Object, configuration);
            processor.Start();
            Thread.Sleep(1000);
            processor.Stop();

            queueClientMock.Verify(x => x.GetMessage(), Times.Exactly(1));
            messageProcessorMock.Verify(x => x.ProcessMessage(It.IsAny<string>(), It.IsAny<ConfigurationEntry>()),Times.Exactly(1));
            queueClientMock.Verify(x => x.DeleteMessage(It.IsAny<IMessage>()), Times.Exactly(1));
            processorFactoryMock.Verify(x => x.CreateMessageProcessor(It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
        }
    }
}
