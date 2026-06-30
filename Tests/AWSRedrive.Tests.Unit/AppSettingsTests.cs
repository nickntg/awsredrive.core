using AWSRedrive.Models;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class AppSettingsTests
    {
        [Fact]
        public void DefaultAppSettings_HasDashboardDefaults()
        {
            var settings = new AppSettings();

            Assert.NotNull(settings.Dashboard);
            Assert.False(settings.Dashboard.Enabled);
            Assert.Equal(5000, settings.Dashboard.Port);
            Assert.Equal(5000, settings.Dashboard.RefreshIntervalMs);
        }

        [Fact]
        public void DefaultAppSettings_HasMetricsDefaults()
        {
            var settings = new AppSettings();

            Assert.NotNull(settings.Metrics);
            Assert.False(settings.Metrics.Enabled);
            Assert.Equal(300, settings.Metrics.IntervalSeconds);
        }

        [Fact]
        public void DashboardSettings_CanBeConfigured()
        {
            var settings = new DashboardSettings
            {
                Enabled = true,
                Port = 8080,
                RefreshIntervalMs = 1000
            };

            Assert.True(settings.Enabled);
            Assert.Equal(8080, settings.Port);
            Assert.Equal(1000, settings.RefreshIntervalMs);
        }

        [Fact]
        public void MetricsSettings_CanBeConfigured()
        {
            var settings = new MetricsSettings
            {
                Enabled = true,
                IntervalSeconds = 60
            };

            Assert.True(settings.Enabled);
            Assert.Equal(60, settings.IntervalSeconds);
        }
    }
}
