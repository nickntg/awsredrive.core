namespace AWSRedrive.Interfaces
{
    public interface IMessageProcessor
    {
        void ProcessMessage(string message, ConfigurationEntry configurationEntry);
    }
}
