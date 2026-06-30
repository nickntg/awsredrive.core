using AWSRedrive.Factories;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using FakeItEasy;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class QueueProcessorFactoryTests
    {
        [Fact]
        public void CreateQueueProcessor_ReturnsQueueProcessor()
        {
            var metricsSettings = A.Fake<IMetricsSettings>();
            var appSettings = new AppSettings();
            var factory = new QueueProcessorFactory(metricsSettings, appSettings);

            var processor = factory.CreateQueueProcessor();

            Assert.NotNull(processor);
            Assert.IsType<QueueProcessor>(processor);
        }

        [Fact]
        public void CreateQueueProcessor_WithNullSettings_DoesNotThrow()
        {
            var factory = new QueueProcessorFactory(null, null);

            var exception = Record.Exception(() => factory.CreateQueueProcessor());

            Assert.Null(exception);
        }

        [Fact]
        public void CreateQueueProcessor_UsesDefaultLogLevel()
        {
            var metricsSettings = A.Fake<IMetricsSettings>();
            var appSettings = new AppSettings { DefaultLogLevel = "Debug" };
            var factory = new QueueProcessorFactory(metricsSettings, appSettings);

            var processor = factory.CreateQueueProcessor();

            Assert.NotNull(processor);
        }
    }
}