using System.Collections.Generic;

namespace AWSRedrive.Interfaces
{
    public interface IConfigurationReader
    { 
        List<ConfigurationEntry> ReadConfiguration();
    }
}