using AWSRedrive.Factories;
using AWSRedrive.Interfaces;
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
            var factory = new QueueProcessorFactory(metricsSettings);

            var processor = factory.CreateQueueProcessor();

            Assert.NotNull(processor);
            Assert.IsType<QueueProcessor>(processor);
        }

        [Fact]
        public void CreateQueueProcessor_WithNullSettings_DoesNotThrow()
        {
            var factory = new QueueProcessorFactory(null);

            var exception = Record.Exception(() => factory.CreateQueueProcessor());

            Assert.Null(exception);
        }
    }
}
