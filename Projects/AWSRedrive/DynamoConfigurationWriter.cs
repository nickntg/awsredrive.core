using System.Collections.Generic;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using Newtonsoft.Json;

namespace AWSRedrive
{
    public class DynamoConfigurationWriter(IAmazonDynamoDB dynamo) : IConfigurationWriter
    {
        public void Save(IList<ConfigurationEntry> configs)
        {
            foreach (var config in configs)
            {
                var key = new Dictionary<string, AttributeValue>
                {
                    {
                        Constants.DynamoId, new AttributeValue { S = config.Alias }
                    }
                };

                var values = new Dictionary<string, AttributeValueUpdate>
                {
                    {
                        Constants.DynamoConfig, new AttributeValueUpdate
                        {
                            Value = new AttributeValue { S = JsonConvert.SerializeObject(config) },
                            Action = AttributeAction.PUT
                        }
                    }
                };

                _ = dynamo.UpdateItemAsync(Constants.DynamoTable, key, values).Result;
            }
        }
    }
}
