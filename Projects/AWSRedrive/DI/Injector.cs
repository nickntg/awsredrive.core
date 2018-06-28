using AWSRedrive.Factories;
using AWSRedrive.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AWSRedrive.DI
{
    public class Injector
    {
        private static ServiceProvider Container { get; }

        static Injector()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfigurationReader, ConfigurationReader>();
            services.AddSingleton<IQueueClientFactory, QueueClientFactory>();
            services.AddSingleton<IMessageProcessorFactory, MessageProcessorFactory>();
            services.AddTransient<IQueueProcessorFactory, QueueProcessorFactory>();
            services.AddTransient<IConfigurationChangeManager, ConfigurationChangeManager>();
            services.AddTransient<IOrchestrator, Orchestrator>();
            Container = services.BuildServiceProvider();
        }

        public static IOrchestrator GetOrchestrator()
        {
            return Container.GetService<IOrchestrator>();
        }
    }
}