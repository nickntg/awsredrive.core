using System.Collections.Generic;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;

namespace AWSRedrive.Tests.Unit.Helpers
{
    public class SimpleConfigurationReader : IConfigurationReader
    {
        public List<ConfigurationEntry> Configs { get; set; }

        public List<ConfigurationEntry> ReadConfiguration()
        {
            return Configs;
        }

        public bool CanBeUsed()
        {
            return true;
        }
    }
}