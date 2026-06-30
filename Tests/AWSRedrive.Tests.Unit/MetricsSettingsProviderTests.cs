using AWSRedrive.Models;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class MetricsSettingsProviderTests
    {
        [Fact]
        public void NullSettings_ReturnsDefaults()
        {
            var provider = new MetricsSettingsProvider(null);

            Assert.False(provider.Enabled);
            Assert.Equal(300, provider.IntervalSeconds);
        }

        [Fact]
        public void WithSettings_ReturnsConfiguredValues()
        {
            var settings = new MetricsSettings
            {
                Enabled = true,
                IntervalSeconds = 60
            };
            var provider = new MetricsSettingsProvider(settings);

            Assert.True(provider.Enabled);
            Assert.Equal(60, provider.IntervalSeconds);
        }

        [Fact]
        public void DefaultSettings_HasCorrectDefaults()
        {
            var settings = new MetricsSettings();
            var provider = new MetricsSettingsProvider(settings);

            Assert.False(provider.Enabled);
            Assert.Equal(300, provider.IntervalSeconds);
        }
    }
}
