using System.Collections.Generic;
using AWSRedrive.Models;

namespace AWSRedrive.Interfaces
{
    public interface IConfigurationReader
    { 
        List<ConfigurationEntry> ReadConfiguration();
        bool CanBeUsed();
    }
}