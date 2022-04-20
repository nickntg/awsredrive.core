using System;
using System.Threading;
using System.Threading.Tasks;
using AWSRedrive.Interfaces;
using Microsoft.Extensions.Hosting;
using NLog;

namespace AWSRedrive.LinuxService
{
    public class Worker : BackgroundService
    {
        private readonly NLog.ILogger  _logger = LogManager.GetCurrentClassLogger();
        private readonly IOrchestrator _orchestrator;

        public Worker(IOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.Info("Service starting orchestrator");
                _orchestrator.Start();

                _logger.Info("Orchestrator started");
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }

                _logger.Info("Stop requested, stopping orchestrator");
                _orchestrator.Stop();
                _logger.Info("Orchestrator stopped");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error while running");
            }
        }
    }
}