using System;
using System.Diagnostics;
using System.IO;
using AWSRedrive;
using AWSRedrive.DI;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Web;

namespace AWSRedrive.LinuxService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            SetCurrentDirectoryForService();

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();

            var appSettings = new AppSettings();
            configuration.Bind(appSettings);

            var host = Host.CreateDefaultBuilder(args)
                .UseNLog()
                .ConfigureServices(services =>
                {
                    Injector.Inject(services, appSettings);

                    services.AddSingleton(appSettings);

                    services.AddSingleton<DashboardServer>(sp =>
                    {
                        var configReader = sp.GetRequiredService<IConfigurationReader>();
                        var orchestrator = sp.GetRequiredService<IOrchestrator>();
                        return new DashboardServer(configReader, orchestrator, appSettings.Dashboard);
                    });

                    services.AddHostedService<Worker>();
                })
                .Build();

            try
            {
                host.Run();
            }
            catch (Exception ex)
            {
                var logger = LogManager.GetCurrentClassLogger();
                logger.Fatal(ex);
            }
            finally
            {
                LogManager.Shutdown();
            }
        }

        private static void SetCurrentDirectoryForService()
        {
            var processModule = Process.GetCurrentProcess().MainModule;
            if (processModule != null)
            {
                var pathToContentRoot = Path.GetDirectoryName(processModule.FileName);
                if (pathToContentRoot != null)
                {
                    Directory.SetCurrentDirectory(pathToContentRoot);
                }
            }
        }
    }
}
