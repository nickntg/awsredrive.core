using AWSRedrive.Interfaces;

namespace AWSRedrive.Factories
{
    public class QueueProcessorFactory : IQueueProcessorFactory
    {
        private readonly IMetricsSettings _metricsSettings;

        public QueueProcessorFactory(IMetricsSettings metricsSettings)
        {
            _metricsSettings = metricsSettings;
        }

        public IQueueProcessor CreateQueueProcessor()
        {
            return new QueueProcessor(_metricsSettings);
        }
    }
}
