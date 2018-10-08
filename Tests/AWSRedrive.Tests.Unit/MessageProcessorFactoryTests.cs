using AWSRedrive.Factories;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class MessageProcessorFactoryTests
    {
        [Fact]
        public void CreatesAnHttpProcessor()
        {
            var factory = new MessageProcessorFactory();
            var o = factory.CreateMessageProcessor(new ConfigurationEntry {RedriveUrl = "some string"});
            Assert.IsType<HttpMessageProcessor>(o);
        }

        [Fact]
        public void CreatesAPowerShellProcessor()
        {
            var factory = new MessageProcessorFactory();
            var o = factory.CreateMessageProcessor(new ConfigurationEntry { RedriveScript = "some string" });
            Assert.IsType<PowerShellMessageProcessor>(o);
        }
    }
}
