namespace AWSRedrive.Interfaces
{
    public interface IQueueClient
    {
        void Init();
        IMessage GetMessage();
        void DeleteMessage(IMessage message);
    }
}
