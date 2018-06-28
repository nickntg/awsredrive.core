using AWSRedrive.Interfaces;

namespace AWSRedrive.Factories
{
    public class QueueClientFactory : IQueueClientFactory
    {
        public IQueueClient CreateClient(ConfigurationEntry configurationEntry)
        {
            return null;
        }
    }
}