using AWSRedrive.Factories;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AWSRedrive.DI
{
    public class Injector
    {
        public static ServiceProvider Container { get; set; }

        public static void Inject()
        {
            Inject(new AppSettings());
        }

        public static void Inject(AppSettings appSettings)
        {
            var services = new ServiceCollection();
            Inject(services, appSettings);
            Container = services.BuildServiceProvider();
        }

        public static void Inject(IServiceCollection services)
        {
            Inject(services, new AppSettings());
        }

        public static void Inject(IServiceCollection services, AppSettings appSettings)
        {
            services.AddSingleton(appSettings);
            services.AddSingleton<IMetricsSettings>(new MetricsSettingsProvider(appSettings.Metrics));
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
