using System.Collections.Generic;
using System.IO;
using System.Linq;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using AWSRedrive.Validations;
using FluentValidation;
using Newtonsoft.Json;
using NLog;

namespace AWSRedrive
{
    public class ConfigurationReader : IConfigurationReader
    {
        private const string ConfigurationFile = "config.json";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public List<ConfigurationEntry> ReadConfiguration()
        {
            Logger.Trace($"Reading {ConfigurationFile}");
            var contents = File.ReadAllText(ConfigurationFile, System.Text.Encoding.Default);
            var list = JsonConvert.DeserializeObject<ConfigurationEntry[]>(contents).ToList();

            var validator = new ConfigurationEntryValidator();

            foreach (var entry in list)
            {
                if (string.IsNullOrEmpty(entry.Alias))
                {
                    entry.Alias = entry.RedriveUrl;
                }

                var result = validator.Validate(entry);
                if (!result.IsValid)
                {                   
                    throw new ValidationException(result.Errors);
                }
            }

            return list;
        }
    }
}