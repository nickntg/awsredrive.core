using AWSRedrive.Interfaces;
using AWSRedrive.Models;

namespace AWSRedrive
{
    public class MetricsSettingsProvider : IMetricsSettings
    {
        private readonly MetricsSettings _settings;

        public MetricsSettingsProvider(MetricsSettings settings)
        {
            _settings = settings ?? new MetricsSettings();
        }

        public bool Enabled => _settings.Enabled;
        public int IntervalSeconds => _settings.IntervalSeconds;
    }
}
