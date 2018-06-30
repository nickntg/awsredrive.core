namespace AWSRedrive.Interfaces
{
    public interface IQueueClient
    {
        ConfigurationEntry ConfigurationEntry { get; set; }
        void Init();
        IMessage GetMessage();
        void DeleteMessage(IMessage message);
    }
}
