using AWSRedrive.Interfaces;

namespace AWSRedrive.Factories
{
    public class QueueProcessorFactory : IQueueProcessorFactory
    {
        public IQueueProcessor CreateQueueProcessor()
        {
            return new QueueProcessor();
        }
    }
}