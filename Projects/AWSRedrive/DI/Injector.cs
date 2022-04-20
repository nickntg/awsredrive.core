using AWSRedrive.Factories;
using AWSRedrive.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AWSRedrive.DI
{
    public class Injector
    {
        public static ServiceProvider Container { get; set; }

        public static void Inject()
        {
            var services = new ServiceCollection();
            Inject(services);
            Container = services.BuildServiceProvider();
        }

        public static void Inject(IServiceCollection services)
        {
            services.AddSingleton<IConfigurationReader, ConfigurationReader>();
            services.AddSingleton<IQueueClientFactory, QueueClientFactory>();
            services.AddSingleton<IMessageProcessorFactory, MessageProcessorFactory>();
            services.AddTransient<IQueueProcessorFactory, QueueProcessorFactory>();
            services.AddTransient<IConfigurationChangeManager, ConfigurationChangeManager>();
            services.AddTransient<IOrchestrator, Orchestrator>();
        }

        protected Injector() { }

        public static IOrchestrator GetOrchestrator()
        {
            return Container.GetService<IOrchestrator>();
        }
    }
}