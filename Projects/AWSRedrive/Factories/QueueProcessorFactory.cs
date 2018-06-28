using AWSRedrive.Interfaces;

namespace AWSRedrive.Factories
{
    public class QueueProcessorFactory : IQueueProcessorFactory
    {
        public IQueueProcessor CreateQueueProcessor(IQueueClient queueClient, IMessageProcessor messageProcessor,
            ConfigurationEntry configuration)
        {
            return null;
        }
    }
}