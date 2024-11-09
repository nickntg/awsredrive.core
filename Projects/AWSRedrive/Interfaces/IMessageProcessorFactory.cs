using AWSRedrive.Models;

namespace AWSRedrive.Interfaces
{
    public interface IMessageProcessorFactory
    {
        IMessageProcessor CreateMessageProcessor(ConfigurationEntry configuration);
    }
}
