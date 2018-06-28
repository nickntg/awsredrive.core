namespace AWSRedrive.Interfaces
{
    public interface IQueueClient
    {
        IMessage GetMessage();
        void DeleteMessage(IMessage message);
    }
}
