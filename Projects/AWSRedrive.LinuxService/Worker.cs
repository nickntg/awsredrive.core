using System;
using System.Threading;
using System.Threading.Tasks;
using AWSRedrive.Interfaces;
using Microsoft.Extensions.Hosting;
using NLog;

namespace AWSRedrive.LinuxService
{
    public class Worker(IOrchestrator orchestrator) : BackgroundService
    {
        private readonly ILogger  _logger = LogManager.GetCurrentClassLogger();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.Info("Service starting orchestrator");
                orchestrator.Start();

                _logger.Info("Orchestrator started");
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }

                _logger.Info("Stop requested, stopping orchestrator");
                orchestrator.Stop();
                _logger.Info("Orchestrator stopped");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error while running");
            }
        }
    }
}