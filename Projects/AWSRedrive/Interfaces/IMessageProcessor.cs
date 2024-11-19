using System.Collections.Generic;
using AWSRedrive.Models;

namespace AWSRedrive.Interfaces
{
    public interface IMessageProcessor
    {
        void ProcessMessage(string message, Dictionary<string, string> attributes,
            ConfigurationEntry configurationEntry);
    }
}
