namespace AWSRedrive.Interfaces
{
    public interface IQueueProcessorFactory
    {
        IQueueProcessor CreateQueueProcessor();
    }
}