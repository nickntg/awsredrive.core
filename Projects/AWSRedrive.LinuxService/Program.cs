using System;
using System.Diagnostics;
using System.IO;
using AWSRedrive.DI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Extensions.Logging;

namespace AWSRedrive.LinuxService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            SetCurrentDirectoryForService();

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    Injector.Inject(services);
                    services.AddHostedService<Worker>();
                    services.AddLogging(builder =>
                    {
                        builder.AddNLog();
                    });
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
