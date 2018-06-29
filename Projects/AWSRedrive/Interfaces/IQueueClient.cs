namespace AWSRedrive.Interfaces
{
    public interface IQueueClient
    {
        ConfigurationEntry ConfigurationEntry { get; set; }
        IMessage GetMessage();
        void DeleteMessage(IMessage message);
    }
}
