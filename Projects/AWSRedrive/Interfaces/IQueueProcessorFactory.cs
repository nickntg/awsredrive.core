namespace AWSRedrive.Interfaces
{
    public interface IQueueProcessorFactory
    {
        IQueueProcessor CreateQueueProcessor(IQueueClient queueClient,
            IMessageProcessor messageProcessor,
            ConfigurationEntry configuration);
    }
}