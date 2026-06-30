using System.Collections.Generic;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using AWSRedrive.Tests.Unit.Helpers;
using FakeItEasy;
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

            var mockProcessor = A.Fake<IQueueProcessor>(x => x.Strict());
            A.CallTo(() => mockProcessor.Configuration)
                .Returns(GetOneConfigurationEntry("#1", true));
            A.CallTo(() => mockProcessor.Stop())
                .DoesNothing();
            A.CallTo(() => mockProcessor.Equals(A<object>.Ignored))
                .CallsBaseMethod();

            var processors = new List<IQueueProcessor>
            {
                mockProcessor
            };

            configChangeManager.ReadChanges(config, processors, null, null, null);
            Assert.NotNull(processors);
            Assert.Empty(processors);

            A.CallTo(() => mockProcessor.Configuration)
                .MustHaveHappenedOnceOrMore();
            A.CallTo(() => mockProcessor.Stop())
                .MustHaveHappenedOnceExactly();
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

            var mockProcessor = A.Fake<IQueueProcessor>(x => x.Strict());
            A.CallTo(() => mockProcessor.Configuration)
                .Returns(GetOneConfigurationEntry("#1", true));
            A.CallTo(() => mockProcessor.Start())
                .DoesNothing();
            A.CallTo(() =>
                    mockProcessor.Init(A<IQueueClient>.Ignored, A<IMessageProcessorFactory>.Ignored,
                        A<ConfigurationEntry>.Ignored))
                .DoesNothing();

            var mockProcessorFactory = A.Fake<IQueueProcessorFactory>(x => x.Strict());
            A.CallTo(() => mockProcessorFactory.CreateQueueProcessor())
                .Returns(mockProcessor);

            var mockClient = A.Fake<IQueueClient>(x => x.Strict());
            A.CallTo(() => mockClient.Init())
                .DoesNothing();
            A.CallTo(() => mockClient.GetDlqUrl())
                .Returns(null);

            var mockClientFactory = A.Fake<IQueueClientFactory>(x => x.Strict());
            A.CallTo(() => mockClientFactory.CreateClient(A<ConfigurationEntry>.Ignored))
                .Returns(mockClient);

            var mockMessageProcessorFactory = A.Fake<IMessageProcessorFactory>(x => x.Strict());

            var processors = new List<IQueueProcessor>();
            configChangeManager.ReadChanges(config, processors, mockClientFactory, mockMessageProcessorFactory, mockProcessorFactory);
            Assert.NotNull(processors);
            Assert.Single(processors);

            A.CallTo(() => mockClient.Init())
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => mockClientFactory.CreateClient(A<ConfigurationEntry>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => mockProcessorFactory.CreateQueueProcessor())
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => mockProcessor.Start())
                .MustHaveHappenedOnceExactly();
            A.CallTo(() =>
                mockProcessor.Init(A<IQueueClient>.Ignored, A<IMessageProcessorFactory>.Ignored,
                    A<ConfigurationEntry>.Ignored))
                .MustHaveHappenedOnceExactly();

            if (readConfigurationAgain)
            {
                configChangeManager.ReadChanges(config, processors, mockClientFactory,
                    mockMessageProcessorFactory, mockProcessorFactory);

                A.CallTo(() => mockClient.Init())
                    .MustHaveHappenedOnceExactly();
                A.CallTo(() => mockClientFactory.CreateClient(A<ConfigurationEntry>.Ignored))
                    .MustHaveHappenedOnceExactly();
                A.CallTo(() => mockProcessorFactory.CreateQueueProcessor())
                    .MustHaveHappenedOnceExactly();
                A.CallTo(() => mockProcessor.Start())
                    .MustHaveHappenedOnceExactly();
                A.CallTo(() =>
                        mockProcessor.Init(A<IQueueClient>.Ignored, A<IMessageProcessorFactory>.Ignored,
                            A<ConfigurationEntry>.Ignored))
                    .MustHaveHappenedOnceExactly();
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

            var mockNewProcessor = A.Fake<IQueueProcessor>(x => x.Strict());
            A.CallTo(() => mockNewProcessor.Configuration)
                .Returns(GetOneConfigurationEntry("#1", true));
            A.CallTo(() => mockNewProcessor.Start())
                .DoesNothing();
            A.CallTo(() => mockNewProcessor.Init(A<IQueueClient>.Ignored, A<IMessageProcessorFactory>.Ignored,
                    A<ConfigurationEntry>.Ignored))
                .DoesNothing();
            A.CallTo(() => mockNewProcessor.Equals(A<object>.Ignored))
                .CallsBaseMethod();

            var mockProcessorFactory = A.Fake<IQueueProcessorFactory>(x => x.Strict());
            A.CallTo(() => mockProcessorFactory.CreateQueueProcessor())
                .Returns(mockNewProcessor);

            var mockClient = A.Fake<IQueueClient>(x => x.Strict());
            A.CallTo(() => mockClient.Init())
                .DoesNothing();
            A.CallTo(() => mockClient.GetDlqUrl())
                .Returns(null);

            var mockClientFactory = A.Fake<IQueueClientFactory>(x => x.Strict());
            A.CallTo(() => mockClientFactory.CreateClient(A<ConfigurationEntry>.Ignored))
                .Returns(mockClient);

            var mockMessageProcessorFactory = A.Fake<IMessageProcessorFactory>(x => x.Strict());

            var mockExistingProcessor = A.Fake<IQueueProcessor>(x => x.Strict());
            A.CallTo(() => mockExistingProcessor.Configuration)
                .Returns(entryToUse);
            A.CallTo(() => mockExistingProcessor.Stop())
                .DoesNothing();
            A.CallTo(() => mockExistingProcessor.Equals(A<object>.Ignored))
                .CallsBaseMethod();
            var processors = new List<IQueueProcessor>
            {
                mockExistingProcessor
            };
            configChangeManager.ReadChanges(config, processors, mockClientFactory, mockMessageProcessorFactory, mockProcessorFactory);
            Assert.NotNull(processors);
            Assert.Single(processors);

            A.CallTo(() => mockClient.Init())
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => mockClientFactory.CreateClient(A<ConfigurationEntry>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => mockProcessorFactory.CreateQueueProcessor())
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => mockNewProcessor.Start())
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => mockNewProcessor.Init(A<IQueueClient>.Ignored, A<IMessageProcessorFactory>.Ignored,
                A<ConfigurationEntry>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => mockNewProcessor.Configuration)
                .MustHaveHappenedOnceOrMore();
            A.CallTo(() => mockExistingProcessor.Stop())
                .MustHaveHappenedOnceExactly();
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

            var mockedProcessors = new List<IQueueProcessor>
            {
                GetOneMockedProcessor(config.Configs[0]),
                GetOneMockedProcessor(config.Configs[1]),
                GetOneMockedProcessor(config.Configs[2])
            };

            var processors = new List<IQueueProcessor>
            {
                mockedProcessors[0],
                mockedProcessors[1],
                mockedProcessors[2]
            };

            configChangeManager.ReadChanges(config, processors, null, null, null);
            Assert.NotNull(processors);
            Assert.Equal(3, processors.Count);

            // Verify no processor was stopped or started - just configuration was checked
            foreach (var processor in mockedProcessors)
            {
                A.CallTo(() => processor.Configuration)
                    .MustHaveHappenedOnceOrMore();
            }
        }

        [Fact]
        public void DoesNotRestartProcessorWhenOnlyLogLevelChanges()
        {
            var configChangeManager = new ConfigurationChangeManager();
            
            var originalConfig = GetOneConfigurationEntry("#1", true);
            originalConfig.LogLevel = "Error";
            
            var changedConfig = GetOneConfigurationEntry("#1", true);
            changedConfig.LogLevel = "Debug"; // Only LogLevel changed

            var config = new SimpleConfigurationReader
            {
                Configs = new List<ConfigurationEntry> { changedConfig }
            };

            var mockProcessor = A.Fake<IQueueProcessor>(x => x.Strict());
            A.CallTo(() => mockProcessor.Configuration)
                .Returns(originalConfig);
            // Note: No Stop() setup - test will fail if Stop() is called

            var processors = new List<IQueueProcessor> { mockProcessor };

            configChangeManager.ReadChanges(config, processors, null, null, null);
            
            Assert.Single(processors);
            A.CallTo(() => mockProcessor.Configuration)
                .MustHaveHappenedOnceOrMore();
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

        private static IQueueProcessor GetOneMockedProcessor(ConfigurationEntry config)
        {
            var mockNewProcessor = A.Fake<IQueueProcessor>(x => x.Strict());
            A.CallTo(() => mockNewProcessor.Configuration)
                .Returns(config);
            return mockNewProcessor;
        }
    }
}