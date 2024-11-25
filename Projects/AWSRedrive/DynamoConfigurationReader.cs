using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using Newtonsoft.Json;
using NLog;

namespace AWSRedrive
{
    public class DynamoConfigurationReader(IAmazonDynamoDB dynamo) : IConfigurationReader
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public List<ConfigurationEntry> ReadConfiguration()
        {
            var lst = new List<ConfigurationEntry>();

            Dictionary<string, AttributeValue> next = null;

            do
            {
                var scanRequest = new ScanRequest(Constants.DynamoTable)
                {
                    Select = Select.ALL_ATTRIBUTES,
                    ConsistentRead = false,
                    ExclusiveStartKey = next
                };

                var results = dynamo.ScanAsync(scanRequest).Result;
                next = results.LastEvaluatedKey;

                lst.AddRange(results.Items.Select(item => item[Constants.DynamoConfig].S)
                    .Select(JsonConvert.DeserializeObject<ConfigurationEntry>));
            } while (next is { Count: > 0 });

            return lst;
        }

        public bool CanBeUsed()
        {
            try
            {
                ReadConfiguration();

                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex);

                return false;
            }
        }
    }
}
