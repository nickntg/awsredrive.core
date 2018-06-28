using AWSRedrive.Interfaces;

namespace AWSRedrive.Tests.Unit.Helpers
{
    public class DummyQueueProcessor : IQueueProcessor
    {
        public ConfigurationEntry Configuration { get; set; }

        public void Init(IQueueClient queueClient, IMessageProcessor messageProcessor, ConfigurationEntry configuration)
        {
            Configuration = configuration;
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void ProcessMessageLoop()
        {
        }
    }
}
