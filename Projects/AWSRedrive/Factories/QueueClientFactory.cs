using AWSRedrive.Interfaces;
using AWSRedrive.Models;

namespace AWSRedrive.Factories
{
    public class QueueClientFactory : IQueueClientFactory
    {
        public IQueueClient CreateClient(ConfigurationEntry configurationEntry)
        {
            return new AwsQueueClient {ConfigurationEntry = configurationEntry};
        }
    }
}