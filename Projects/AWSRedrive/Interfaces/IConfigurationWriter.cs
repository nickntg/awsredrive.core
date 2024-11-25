using System.Collections.Generic;
using AWSRedrive.Models;

namespace AWSRedrive.Interfaces
{
    public interface IConfigurationWriter
    {
        void Save(IList<ConfigurationEntry> configs);
    }
}
