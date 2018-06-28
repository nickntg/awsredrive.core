namespace AWSRedrive.Interfaces
{
    public interface IQueueProcessor
    {
        ConfigurationEntry Configuration { get; set; }
        void Init(IQueueClient queueClient,
            IMessageProcessor messageProcessor,
            ConfigurationEntry configuration
        );
        void Start();
        void Stop();
        void ProcessMessageLoop();
    }
}