namespace AWSRedrive.Interfaces
{
    public interface IQueueClientFactory
    {
        IQueueClient CreateClient(ConfigurationEntry configurationEntry);
    }
}