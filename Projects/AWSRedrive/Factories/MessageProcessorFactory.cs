using AWSRedrive.Interfaces;

namespace AWSRedrive.Factories
{
    public class MessageProcessorFactory : IMessageProcessorFactory
    {
        public IMessageProcessor CreateMessageProcessor(ConfigurationEntry configuration)
        {
            return string.IsNullOrEmpty(configuration.RedriveUrl)
                ? string.IsNullOrEmpty(configuration.RedriveScript)
                    ? new KafkaMessageProcessor()
                    : new PowerShellMessageProcessor()
                : new HttpMessageProcessor();
        }
    }
}