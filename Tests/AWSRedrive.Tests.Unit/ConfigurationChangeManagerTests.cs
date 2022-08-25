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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void StartsOneProcessorAndOptionallyDoesNotRestartTheSameOne(bool readConfigurationAgain)
        {
            var configChangeManager = new ConfigurationChangeManager();
            var config = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry> { GetOneConfigurationEntry("#1", true) }
            };

            var mockProcessor = new Mock<IQueueProcessor>(MockBehavior.Strict);
            mockProcessor.SetupGet(x => x.Configuration).Returns(GetOneConfigurationEntry("#1", true));
            mockProcessor.Setup(x => x.Start()).Verifiable();
            mockProcessor.Setup(x => x.Init(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessorFactory>(), It.IsAny<ConfigurationEntry>())).Verifiable();

            var mockProcessorFactory = new Mock<IQueueProcessorFactory>(MockBehavior.Strict);
            mockProcessorFactory.Setup(x => x.CreateQueueProcessor()).Returns(mockProcessor.Object).Verifiable();

            var mockClient = new Mock<IQueueClient>(MockBehavior.Strict);
            mockClient.Setup(x => x.Init()).Verifiable();

            var mockClientFactory = new Mock<IQueueClientFactory>(MockBehavior.Strict);
            mockClientFactory.Setup(x => x.CreateClient(It.IsAny<ConfigurationEntry>())).Returns(mockClient.Object).Verifiable();

            var mockMessageProcessorFactory = new Mock<IMessageProcessorFactory>(MockBehavior.Strict);

            var processors = new List<IQueueProcessor>();
            configChangeManager.ReadChanges(config, processors, mockClientFactory.Object, mockMessageProcessorFactory.Object, mockProcessorFactory.Object);
            Assert.NotNull(processors);
            Assert.Single(processors);

            mockClient.Verify(x => x.Init(), Times.Exactly(1));
            mockClientFactory.Verify(x => x.CreateClient(It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
            mockProcessorFactory.Verify(x => x.CreateQueueProcessor(), Times.Exactly(1));
            mockProcessor.Verify(x => x.Start(), Times.Exactly(1));
            mockProcessor.Verify(x => x.Init(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessorFactory>(), It.IsAny<ConfigurationEntry>()), Times.Exactly(1));

            if (readConfigurationAgain)
            {
                configChangeManager.ReadChanges(config, processors, mockClientFactory.Object,
                    mockMessageProcessorFactory.Object, mockProcessorFactory.Object);

                mockClient.Verify(x => x.Init(), Times.Exactly(1));
                mockClientFactory.Verify(x => x.CreateClient(It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
                mockProcessorFactory.Verify(x => x.CreateQueueProcessor(), Times.Exactly(1));
                mockProcessor.Verify(x => x.Start(), Times.Exactly(1));
                mockProcessor.Verify(
                    x => x.Init(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessorFactory>(),
                        It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
            }
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
            StartAndStopOneProcessor(GetOneConfigurationEntry("#2", true));
        }

        [Fact]
        public void StartsOneAndStopsOneProcessorThatWasChanged()
        {
            var existingProcessor = GetOneConfigurationEntry("#1", true);
            existingProcessor.BasicAuthPassword = "1234";

            StartAndStopOneProcessor(existingProcessor);
        }

        private void StartAndStopOneProcessor(ConfigurationEntry entryToUse)
        {
            var configChangeManager = new ConfigurationChangeManager();
            var config = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry> { GetOneConfigurationEntry("#1", true) }
            };

            var mockNewProcessor = new Mock<IQueueProcessor>(MockBehavior.Strict);
            mockNewProcessor.SetupGet(x => x.Configuration).Returns(GetOneConfigurationEntry("#1", true));
            mockNewProcessor.Setup(x => x.Start()).Verifiable();
            mockNewProcessor.Setup(x => x.Init(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessorFactory>(), It.IsAny<ConfigurationEntry>())).Verifiable();

            var mockProcessorFactory = new Mock<IQueueProcessorFactory>(MockBehavior.Strict);
            mockProcessorFactory.Setup(x => x.CreateQueueProcessor()).Returns(mockNewProcessor.Object).Verifiable();

            var mockClient = new Mock<IQueueClient>(MockBehavior.Strict);
            mockClient.Setup(x => x.Init()).Verifiable();

            var mockClientFactory = new Mock<IQueueClientFactory>(MockBehavior.Strict);
            mockClientFactory.Setup(x => x.CreateClient(It.IsAny<ConfigurationEntry>())).Returns(mockClient.Object).Verifiable();

            var mockMessageProcessorFactory = new Mock<IMessageProcessorFactory>(MockBehavior.Strict);

            var mockExistingProcessor = new Mock<IQueueProcessor>(MockBehavior.Strict);
            mockExistingProcessor.Setup(x => x.Configuration).Returns(entryToUse).Verifiable();
            mockExistingProcessor.Setup(x => x.Stop()).Verifiable();
            var processors = new List<IQueueProcessor>
            {
                mockExistingProcessor.Object
            };
            configChangeManager.ReadChanges(config, processors, mockClientFactory.Object, mockMessageProcessorFactory.Object, mockProcessorFactory.Object);
            Assert.NotNull(processors);
            Assert.Single(processors);

            mockClient.Verify(x => x.Init(), Times.Exactly(1));
            mockClientFactory.Verify(x => x.CreateClient(It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
            mockProcessorFactory.Verify(x => x.CreateQueueProcessor(), Times.Exactly(1));
            mockNewProcessor.Verify(x => x.Start(), Times.Exactly(1));
            mockNewProcessor.Verify(x => x.Init(It.IsAny<IQueueClient>(), It.IsAny<IMessageProcessorFactory>(), It.IsAny<ConfigurationEntry>()), Times.Exactly(1));
            mockExistingProcessor.VerifyGet(x => x.Configuration, Times.AtLeastOnce());
            mockExistingProcessor.Verify(x => x.Stop(), Times.Exactly(1));
        }

        [Fact]
        public void DoesNothingWithUnchangedConfigurationEntries()
        {
            var configChangeManager = new ConfigurationChangeManager();
            var config = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry>
                {
                    GetOneConfigurationEntry("#1", true),
                    GetOneConfigurationEntry("#2", true),
                    GetOneConfigurationEntry("#3", true)
                }
            };

            var mockedProcessors = new List<Mock<IQueueProcessor>>
            {
                GetOneMockedProcessor(config.Configs[0]),
                GetOneMockedProcessor(config.Configs[1]),
                GetOneMockedProcessor(config.Configs[2])
            };

            var processors = new List<IQueueProcessor>
            {
                mockedProcessors[0].Object,
                mockedProcessors[1].Object,
                mockedProcessors[2].Object
            };

            configChangeManager.ReadChanges(config, processors, null, null, null);
            Assert.NotNull(processors);
            Assert.Equal(3, processors.Count);

            foreach (var processor in mockedProcessors)
            {
                processor.VerifyGet(x => x.Configuration, Times.Exactly(18));
            }
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
                AuthToken = null,
                SecretKey = null,
                Region = null
            };
        }

        private static Mock<IQueueProcessor> GetOneMockedProcessor(ConfigurationEntry config)
        {
            var mockNewProcessor = new Mock<IQueueProcessor>(MockBehavior.Strict);
            mockNewProcessor.SetupGet(x => x.Configuration).Returns(config);
            return mockNewProcessor;
        }
    }
}