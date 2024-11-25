using System;
using System.IO;
using Amazon.DynamoDBv2;
using AWSRedrive.Factories;
using AWSRedrive.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AWSRedrive.DI
{
    public class Injector
    {
        public delegate IConfigurationReader ConfigurationReaderResolver(string key);
        public static ServiceProvider Container { get; set; }

        public static void Inject()
        {
            var services = new ServiceCollection();
            Inject(services);
            Container = services.BuildServiceProvider();
        }

        public static void Inject(IServiceCollection services)
        {
            var configuration = SetupConfiguration();
            var options = configuration.GetAWSOptions();

            services.AddScoped(_ => options.CreateServiceClient<IAmazonDynamoDB>());

            services.AddSingleton<LocalConfigurationReader>();
            services.AddSingleton<DynamoConfigurationReader>();
            services.AddSingleton<IConfigurationWriter, DynamoConfigurationWriter>();
            services.AddSingleton<ConfigurationReaderResolver>(sp => key =>
            {
                switch (key)
                {
                    case Constants.LocalConfigurationReader:
                        return sp.GetService<LocalConfigurationReader>();
                    case Constants.DynamoConfigurationReader:
                        return sp.GetService<DynamoConfigurationReader>();
                    default:
                        throw new ArgumentException($"Configuration reader {key} not supported");
                }
            });
            services.AddSingleton<IQueueClientFactory, QueueClientFactory>();
            services.AddSingleton<IMessageProcessorFactory, MessageProcessorFactory>();
            services.AddTransient<IQueueProcessorFactory, QueueProcessorFactory>();
            services.AddTransient<IConfigurationChangeManager, ConfigurationChangeManager>();
            services.AddTransient<IOrchestrator, Orchestrator>();
        }

        private static IConfiguration SetupConfiguration()
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables()
                .Build();
        }

        protected Injector() { }

        public static IOrchestrator GetOrchestrator()
        {
            return Container.GetService<IOrchestrator>();
        }
    }
}