using System;
using System.Threading;
using System.Threading.Tasks;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using Microsoft.Extensions.Hosting;
using NLog;

namespace AWSRedrive.LinuxService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly IOrchestrator _orchestrator;
        private readonly DashboardServer _dashboard;
        private readonly AppSettings _appSettings;

        public Worker(IOrchestrator orchestrator, DashboardServer dashboard, AppSettings appSettings)
        {
            _orchestrator = orchestrator;
            _dashboard = dashboard;
            _appSettings = appSettings;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.Info("AWSRedrive service starting...");
                _logger.Info($"Dashboard.Enabled: {_appSettings.Dashboard.Enabled}");
                _logger.Info($"Dashboard.Port: {_appSettings.Dashboard.Port}");
                _logger.Info($"Metrics.Enabled: {_appSettings.Metrics.Enabled}");
                _logger.Info($"Metrics.IntervalSeconds: {_appSettings.Metrics.IntervalSeconds}");

                if (_appSettings.Dashboard.Enabled)
                {
                    _logger.Info($"Starting dashboard server on port {_appSettings.Dashboard.Port}");
                    _dashboard.Start();
                    _logger.Info("Dashboard server started");
                }
                else
                {
                    _logger.Info("Dashboard is disabled");
                }

                _logger.Info("Starting orchestrator");
                _orchestrator.Start();
                _logger.Info("Orchestrator started");

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }

                _logger.Info("Stop requested, stopping orchestrator");
                _orchestrator.Stop();
                _logger.Info("Orchestrator stopped");

                if (_appSettings.Dashboard.Enabled)
                {
                    _logger.Info("Stopping dashboard server");
                    _dashboard.Stop();
                    _logger.Info("Dashboard server stopped");
                }

                _logger.Info("AWSRedrive service stopped");
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown, ignore
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error while running");
            }
        }
    }
}
