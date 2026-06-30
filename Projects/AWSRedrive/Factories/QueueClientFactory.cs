using AWSRedrive.Interfaces;
using AWSRedrive.Models;

namespace AWSRedrive.Factories
{
    public class QueueClientFactory : IQueueClientFactory
    {
        public IQueueClient CreateClient(ConfigurationEntry configurationEntry)
        {
            var client = new AwsQueueClient { ConfigurationEntry = configurationEntry };
            client.Init();  // ← Add this line
            return client;
        }
    }
}
