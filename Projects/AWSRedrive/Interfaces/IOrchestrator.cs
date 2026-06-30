using System.Collections.Generic;
using AWSRedrive.Models;

namespace AWSRedrive.Interfaces
{
    public interface IOrchestrator
    {
        void Start();
        void Stop();
        bool SetLogLevel(string alias, string level);
        string GetLogLevel(string alias);
        
        /// <summary>
        /// Returns configurations from running processors (includes runtime data like DlqUrl)
        /// </summary>
        List<ConfigurationEntry> GetConfigurations();
    }
}