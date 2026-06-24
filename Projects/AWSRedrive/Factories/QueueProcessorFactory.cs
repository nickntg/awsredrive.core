using AWSRedrive.Interfaces;
using AWSRedrive.Models;

namespace AWSRedrive.Factories
{
    public class QueueProcessorFactory : IQueueProcessorFactory
    {
        private readonly IMetricsSettings _metricsSettings;
        private readonly string _defaultLogLevel;

        public QueueProcessorFactory(IMetricsSettings metricsSettings, AppSettings appSettings)
        {
            _metricsSettings = metricsSettings;
            _defaultLogLevel = appSettings?.DefaultLogLevel ?? "Error";
        }

        public IQueueProcessor CreateQueueProcessor()
        {
            return new QueueProcessor(_metricsSettings, _defaultLogLevel);
        }
    }
}