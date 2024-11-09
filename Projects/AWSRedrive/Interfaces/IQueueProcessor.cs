using AWSRedrive.Models;

namespace AWSRedrive.Interfaces
{
    public interface IQueueProcessor
    {
        ConfigurationEntry Configuration { get; set; }
        void Init(IQueueClient queueClient,
            IMessageProcessorFactory messageProcessorFactory,
            ConfigurationEntry configuration
        );
        void Start();
        void Stop();
        void ProcessMessageLoop();
    }
}