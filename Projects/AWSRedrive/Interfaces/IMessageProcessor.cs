using System.Collections.Generic;

namespace AWSRedrive.Interfaces
{
    public interface IMessageProcessor
    {
        void ProcessMessage(string message, Dictionary<string, string> attributes,
            ConfigurationEntry configurationEntry);
    }
}
