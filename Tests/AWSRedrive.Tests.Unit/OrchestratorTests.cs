using AWSRedrive.Interfaces;
using FakeItEasy;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class OrchestratorTests
    {
        private Orchestrator CreateOrchestrator()
        {
            var configReader = A.Fake<IConfigurationReader>();
            var queueClientFactory = A.Fake<IQueueClientFactory>();
            var messageProcessorFactory = A.Fake<IMessageProcessorFactory>();
            var queueProcessorFactory = A.Fake<IQueueProcessorFactory>();
            var configChangeManager = A.Fake<IConfigurationChangeManager>();

            return new Orchestrator(
                configReader,
                queueClientFactory,
                messageProcessorFactory,
                queueProcessorFactory,
                configChangeManager);
        }

        [Fact]
        public void GetLogLevel_BeforeStart_ReturnsNull()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            // Don't call Start() - _processors is null

            // Act
            var result = orchestrator.GetLogLevel("any-alias");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetLogLevel_BeforeStart_DoesNotThrow()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            // Don't call Start() - _processors is null

            // Act & Assert
            var exception = Record.Exception(() => orchestrator.GetLogLevel("any-alias"));
            Assert.Null(exception);
        }

        [Fact]
        public void SetLogLevel_BeforeStart_ReturnsFalse()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            // Don't call Start() - _processors is null

            // Act
            var result = orchestrator.SetLogLevel("any-alias", "Debug");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void SetLogLevel_BeforeStart_DoesNotThrow()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            // Don't call Start() - _processors is null

            // Act & Assert
            var exception = Record.Exception(() => orchestrator.SetLogLevel("any-alias", "Debug"));
            Assert.Null(exception);
        }

        [Fact]
        public void GetLogLevel_AfterStart_WithUnknownAlias_ReturnsNull()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            orchestrator.Start();

            try
            {
                // Act
                var result = orchestrator.GetLogLevel("unknown-alias");

                // Assert
                Assert.Null(result);
            }
            finally
            {
                orchestrator.Stop();
            }
        }

        [Fact]
        public void SetLogLevel_AfterStart_WithUnknownAlias_ReturnsFalse()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            orchestrator.Start();

            try
            {
                // Act
                var result = orchestrator.SetLogLevel("unknown-alias", "Debug");

                // Assert
                Assert.False(result);
            }
            finally
            {
                orchestrator.Stop();
            }
        }

        [Fact]
        public void IsProcessing_BeforeStart_IsFalse()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();

            // Assert
            Assert.False(orchestrator.IsProcessing);
        }

        [Fact]
        public void IsProcessing_AfterStart_IsTrue()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();

            // Act
            orchestrator.Start();

            try
            {
                // Assert
                Assert.True(orchestrator.IsProcessing);
            }
            finally
            {
                orchestrator.Stop();
            }
        }

        [Fact]
        public void IsProcessing_AfterStop_IsFalse()
        {
            // Arrange
            var orchestrator = CreateOrchestrator();
            orchestrator.Start();

            // Act
            orchestrator.Stop();

            // Assert
            Assert.False(orchestrator.IsProcessing);
        }
    }
}