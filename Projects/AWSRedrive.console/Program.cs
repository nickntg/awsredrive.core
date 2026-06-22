using System;
using System.IO;
using System.Threading;
using AWSRedrive;
using AWSRedrive.DI;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace AWSRedrive.console
{
    class Program
    {
        private static Logger Logger;
        private static readonly ManualResetEventSlim ShutdownEvent = new();
        private static readonly object ShutdownLock = new();
        private static bool _isShuttingDown;

        static void Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();

            var appSettings = new AppSettings();
            configuration.Bind(appSettings);

            Logger = LogManager.GetCurrentClassLogger();

            Logger.Info("AWSRedrive console starting...");
            Logger.Info($"Dashboard.Enabled: {appSettings.Dashboard.Enabled}");
            Logger.Info($"Dashboard.Port: {appSettings.Dashboard.Port}");
            Logger.Info($"Metrics.Enabled: {appSettings.Metrics.Enabled}");
            Logger.Info($"Metrics.IntervalSeconds: {appSettings.Metrics.IntervalSeconds}");

            Injector.Inject(appSettings);

            var orchestrator = Injector.Container.GetRequiredService<IOrchestrator>();

            // Start orchestrator first so _processors is initialized before dashboard accepts requests
            orchestrator.Start();
            Logger.Info("Orchestrator started");

            DashboardServer dashboard = null;
            if (appSettings.Dashboard.Enabled)
            {
                var configReader = Injector.Container.GetRequiredService<IConfigurationReader>();
                dashboard = new DashboardServer(configReader, orchestrator, appSettings.Dashboard);
                dashboard.Start();
                Logger.Info($"Dashboard running at http://localhost:{appSettings.Dashboard.Port}");
            }

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                lock (ShutdownLock)
                {
                    if (_isShuttingDown) return;
                    _isShuttingDown = true;
                }
                Logger.Info("Shutdown signal received");
                ShutdownEvent.Set();
            };

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                lock (ShutdownLock)
                {
                    if (_isShuttingDown) return;
                    _isShuttingDown = true;
                }
                Logger.Info("Process exit signal received");
                ShutdownEvent.Set();
            };

            Logger.Info("Press Ctrl+C to stop");
            ShutdownEvent.Wait();

            Logger.Info("Stopping orchestrator...");
            orchestrator.Stop();
            Logger.Info("Orchestrator stopped");

            if (dashboard != null)
            {
                Logger.Info("Stopping dashboard...");
                dashboard.Stop();
                Logger.Info("Dashboard stopped");
            }

            Logger.Info("AWSRedrive console stopped");
            LogManager.Shutdown();
        }
    }
}