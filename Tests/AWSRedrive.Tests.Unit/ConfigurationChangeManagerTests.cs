using System.Collections.Generic;
using AWSRedrive.Interfaces;
using AWSRedrive.Tests.Unit.Helpers;
using Moq;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class ConfigurationChangeManagerTests
    {
        [Fact]
        public void DoesNothingWithoutConfigurationAndWithoutProcessors()
        {
            var configChangeManager = new ConfigurationChangeManager();
            var config = new SimpleConfigurationReader {Configs = new List<ConfigurationEntry>()};
            var processors = new List<IQueueProcessor>();
            configChangeManager.ReadChanges(config, processors, null, null, null);
            Assert.NotNull(processors);
            Assert.Empty(processors);
        }

        [Fact]
        public void StopsOneProcessor()
        {
            var configChangeManager = new ConfigurationChangeManager();
            var config = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry> {GetOneConfigurationEntry("#1", false)}
            };

            var mockProcessor = new Mock<IQueueProcessor>(MockBehavior.Strict);
            mockProcessor.Setup(x => x.Configuration).Returns(GetOneConfigurationEntry("#1", true)).Verifiable();
            mockProcessor.Setup(x => x.Stop()).Verifiable();
            var processors = new List<IQueueProcessor>
            {
                mockProcessor.Object
            };

            configChangeManager.ReadChanges(config, processors, null, null, null);
            Assert.NotNull(processors);
            Assert.Empty(processors);

            mockProcessor.VerifyGet(x => x.Configuration, Times.AtLeastOnce());
            mockProcessor.Verify(x => x.Stop(), Times.Exactly(1));
        }

        [Fact]
        public void StartsOneProcessor()
        {
            var configChangeManager = new ConfigurationChangeManager();
            var config = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry> { GetOneConfigurationEntry("#1", true) }
            };

            var mockProcessor = new Mock<IQueueProcessor>(MockBehavior.Strict);
            mockProcessor.SetupGet(x => x.Configuration).Returns(GetOneConfigurationEntry("#1", true));
            mockProcessor.Setup(x => x.Start()).Verifiable();
            mockProcessor.Setup(x => x.Init(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessor>(), It.IsAny<ConfigurationEntry>())).Verifiable();

            var mockProcesorFactory = new Mock<IQueueProcessorFactory>(MockBehavior.Strict);
            mockProcesorFactory.Setup(x => x.CreateQueueProcessor(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessor>(), It.IsAny<ConfigurationEntry>())).Returns(mockProcessor.Object).Verifiable();

            var mockClient = new Mock<IQueueClient>(MockBehavior.Strict);

            var mockClientFactory = new Mock<IQueueClientFactory>(MockBehavior.Strict);
            mockClientFactory.Setup(x => x.CreateClient(It.IsAny<ConfigurationEntry>())).Returns(mockClient.Object).Verifiable();

            var mockMessageProcessor = new Mock<IMessageProcessor>(MockBehavior.Strict);

            var mockMessageProcessorFactory = new Mock<IMessageProcessorFactory>(MockBehavior.Strict);
            mockMessageProcessorFactory.Setup(x => x.CreateMessageProcessor()).Returns(mockMessageProcessor.Object).Verifiable();

            var processors = new List<IQueueProcessor>();
            configChangeManager.ReadChanges(config, processors, mockClientFactory.Object, mockMessageProcessorFactory.Object, mockProcesorFactory.Object);
            Assert.NotNull(processors);
            Assert.Single(processors);

            mockClientFactory.Verify(x => x.CreateClient(It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
            mockMessageProcessorFactory.Verify(x => x.CreateMessageProcessor(), Times.Exactly(1));
            mockProcesorFactory.Verify(x => x.CreateQueueProcessor(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessor>(), It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
            mockProcessor.Verify(x => x.Start(), Times.Exactly(1));
            mockProcessor.Verify(x => x.Init(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessor>(), It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
        }

        [Fact]
        public void IgnoresInactiveEntries()
        {
            var configChangeManager = new ConfigurationChangeManager();
            var config = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry> { GetOneConfigurationEntry("#1", false) }
            };

            var processors = new List<IQueueProcessor>();
            configChangeManager.ReadChanges(config, processors, null, null, null);
            Assert.NotNull(processors);
            Assert.Empty(processors);
        }

        [Fact]
        public void StartsOneAndStopsOneProcessor()
        {
            var configChangeManager = new ConfigurationChangeManager();
            var config = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry> { GetOneConfigurationEntry("#1", true) }
            };

            var mockNewProcessor = new Mock<IQueueProcessor>(MockBehavior.Strict);
            mockNewProcessor.SetupGet(x => x.Configuration).Returns(GetOneConfigurationEntry("#1", true));
            mockNewProcessor.Setup(x => x.Start()).Verifiable();
            mockNewProcessor.Setup(x =>x.Init(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessor>(), It.IsAny<ConfigurationEntry>())).Verifiable();

            var mockProcesorFactory = new Mock<IQueueProcessorFactory>(MockBehavior.Strict);
            mockProcesorFactory.Setup(x => x.CreateQueueProcessor(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessor>(), It.IsAny<ConfigurationEntry>())).Returns(mockNewProcessor.Object).Verifiable();

            var mockClient = new Mock<IQueueClient>(MockBehavior.Strict);

            var mockClientFactory = new Mock<IQueueClientFactory>(MockBehavior.Strict);
            mockClientFactory.Setup(x => x.CreateClient(It.IsAny<ConfigurationEntry>())).Returns(mockClient.Object).Verifiable();

            var mockMessageProcessor = new Mock<IMessageProcessor>(MockBehavior.Strict);

            var mockMessageProcessorFactory = new Mock<IMessageProcessorFactory>(MockBehavior.Strict);
            mockMessageProcessorFactory.Setup(x => x.CreateMessageProcessor()).Returns(mockMessageProcessor.Object).Verifiable();

            var mockExistingProcessor = new Mock<IQueueProcessor>(MockBehavior.Strict);
            mockExistingProcessor.Setup(x => x.Configuration).Returns(GetOneConfigurationEntry("#2", true)).Verifiable();
            mockExistingProcessor.Setup(x => x.Stop()).Verifiable();
            var processors = new List<IQueueProcessor>
            {
                mockExistingProcessor.Object
            };
            configChangeManager.ReadChanges(config, processors, mockClientFactory.Object, mockMessageProcessorFactory.Object, mockProcesorFactory.Object);
            Assert.NotNull(processors);
            Assert.Single(processors);

            mockClientFactory.Verify(x => x.CreateClient(It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
            mockMessageProcessorFactory.Verify(x => x.CreateMessageProcessor(), Times.Exactly(1));
            mockProcesorFactory.Verify(x => x.CreateQueueProcessor(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessor>(), It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
            mockNewProcessor.Verify(x => x.Start(), Times.Exactly(1));
            mockNewProcessor.Verify(x => x.Init(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessor>(), It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
            mockExistingProcessor.VerifyGet(x => x.Configuration, Times.AtLeastOnce());
            mockExistingProcessor.Verify(x => x.Stop(), Times.Exactly(1));
        }

        private static ConfigurationEntry GetOneConfigurationEntry(string alias, bool active)
        {
            return new ConfigurationEntry
            {
                Active = active,
                AccessKey = null,
                Alias = alias,
                RedriveUrl = alias,
                QueueUrl = alias,
                AwsGatewayToken = null,
                SecretKey = null,
                Region = null
            };
        }
    }
}