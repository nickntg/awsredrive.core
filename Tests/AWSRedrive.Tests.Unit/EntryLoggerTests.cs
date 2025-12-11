using AWSRedrive;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class EntryLoggerTests
    {
        [Fact]
        public void Constructor_WithValidLogLevel_SetsLogLevel()
        {
            var logger = new EntryLogger("test", "Debug");
            Assert.Equal("Debug", logger.CurrentLogLevel);
        }

        [Fact]
        public void Constructor_WithNullLogLevel_DefaultsToError()
        {
            var logger = new EntryLogger("test", null);
            Assert.Equal("Error", logger.CurrentLogLevel);
        }

        [Fact]
        public void Constructor_WithEmptyLogLevel_DefaultsToError()
        {
            var logger = new EntryLogger("test", "");
            Assert.Equal("Error", logger.CurrentLogLevel);
        }

        [Fact]
        public void InvalidLogLevel_DefaultsToError()
        {
            var logger = new EntryLogger("test", "InvalidLevel");
            Assert.Equal("Error", logger.CurrentLogLevel);
        }

        [Fact]
        public void SetLogLevel_ChangesLogLevel()
        {
            var logger = new EntryLogger("test", "Error");
            Assert.Equal("Error", logger.CurrentLogLevel);

            logger.SetLogLevel("Debug");
            Assert.Equal("Debug", logger.CurrentLogLevel);
        }

        [Fact]
        public void IsTraceEnabled_WhenLevelIsTrace_ReturnsTrue()
        {
            var logger = new EntryLogger("test", "Trace");
            Assert.True(logger.IsTraceEnabled);
        }

        [Fact]
        public void IsTraceEnabled_WhenLevelIsDebug_ReturnsFalse()
        {
            var logger = new EntryLogger("test", "Debug");
            Assert.False(logger.IsTraceEnabled);
        }

        [Fact]
        public void IsDebugEnabled_WhenLevelIsDebug_ReturnsTrue()
        {
            var logger = new EntryLogger("test", "Debug");
            Assert.True(logger.IsDebugEnabled);
        }

        [Fact]
        public void IsDebugEnabled_WhenLevelIsInfo_ReturnsFalse()
        {
            var logger = new EntryLogger("test", "Info");
            Assert.False(logger.IsDebugEnabled);
        }

        [Fact]
        public void IsInfoEnabled_WhenLevelIsInfo_ReturnsTrue()
        {
            var logger = new EntryLogger("test", "Info");
            Assert.True(logger.IsInfoEnabled);
        }

        [Fact]
        public void IsInfoEnabled_WhenLevelIsError_ReturnsFalse()
        {
            var logger = new EntryLogger("test", "Error");
            Assert.False(logger.IsInfoEnabled);
        }
    }
}
