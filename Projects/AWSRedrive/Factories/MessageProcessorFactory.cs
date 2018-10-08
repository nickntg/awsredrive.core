using AWSRedrive.Interfaces;

namespace AWSRedrive.Factories
{
    public class MessageProcessorFactory : IMessageProcessorFactory
    {
        public IMessageProcessor CreateMessageProcessor(ConfigurationEntry configuration)
        {
            return string.IsNullOrEmpty(configuration.RedriveUrl)
                ? (IMessageProcessor) new PowerShellMessageProcessor()
                : (IMessageProcessor) new HttpMessageProcessor();
        }
    }
}