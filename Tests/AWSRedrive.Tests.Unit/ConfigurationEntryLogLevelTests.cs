using AWSRedrive.Models;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class ConfigurationEntryLogLevelTests
    {
        [Fact]
        public void LogLevel_DefaultsToNull()
        {
            var entry = new ConfigurationEntry();

            Assert.Null(entry.LogLevel);
        }

        [Theory]
        [InlineData("Trace")]
        [InlineData("Debug")]
        [InlineData("Info")]
        [InlineData("Warn")]
        [InlineData("Error")]
        [InlineData("Fatal")]
        public void LogLevel_CanBeSet(string level)
        {
            var entry = new ConfigurationEntry { LogLevel = level };

            Assert.Equal(level, entry.LogLevel);
        }
    }
}
