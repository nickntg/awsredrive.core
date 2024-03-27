using AWSRedrive.Validations;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class ConfigurationEntryValidatorTests
    {
        [Fact]
        public void NoQueueUrl()
        {
            var validator = new ConfigurationEntryValidator();
            var entry = new ConfigurationEntry();
            var result = validator.Validate(entry);

            Assert.False(result.IsValid);
            Assert.Contains("Queue Url", result.Errors[0].ErrorMessage);
        }

        [Fact]
        public void NoRedriveDestinations()
        {
            var validator = new ConfigurationEntryValidator();
            var entry = new ConfigurationEntry
            {
                QueueUrl = "queue url"
            };
            var result = validator.Validate(entry);
            Assert.False(result.IsValid);
            Assert.Contains("At least one of RedriveUrl, RedriveScript or RedriveKafkaTopic", result.Errors[0].ErrorMessage);
        }

        [Fact]
        public void MultipleRedriveDestinations()
        {
            var validator = new ConfigurationEntryValidator();
            var entry = new ConfigurationEntry
            {
                QueueUrl = "queue url",
                RedriveUrl = "redrive url",
                RedriveScript = "redrive script",
                RedriveKafkaTopic = "redrive kafka topic"
            };
            var result = validator.Validate(entry);

            Assert.False(result.IsValid);
            Assert.Contains("Only one of RedriveUrl, RedriveScript or RedriveKafkaTopic can be specified.", result.Errors[0].ErrorMessage);
        }

        [Theory]
        [InlineData(null, "redrive script", null)]
        [InlineData("redrive url", null, null)]
        [InlineData(null, null, "kafka topic")]
        public void OnlyOneRedriveDestinationAllowed(string redriveUrl, string redriveScript, string redriveKafkaTopic)
        {
            var validator = new ConfigurationEntryValidator();
            var entry = new ConfigurationEntry
            {
                QueueUrl = "queue url",
                RedriveUrl = redriveUrl,
                RedriveScript = redriveScript,
                RedriveKafkaTopic = redriveKafkaTopic
            };

            var result = validator.Validate(entry);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void NoGetIfDeleteOrPut()
        {
            var validator = new ConfigurationEntryValidator();
            var entry = new ConfigurationEntry
            {
                QueueUrl = "queue url",
                RedriveUrl = "redrive url",
                UseGET = true,
                UsePUT = true
            };
            var result = validator.Validate(entry);

            Assert.False(result.IsValid);
            Assert.Contains("Use GET", result.Errors[0].ErrorMessage);

            entry.UsePUT = false;
            entry.UseDelete = true;

            result = validator.Validate(entry);

            Assert.False(result.IsValid);
            Assert.Contains("Use GET", result.Errors[0].ErrorMessage);
        }

        [Fact]
        public void NoDeleteIfPut()
        {
            var validator = new ConfigurationEntryValidator();
            var entry = new ConfigurationEntry
            {
                QueueUrl = "queue url",
                RedriveUrl = "redrive url",
                UseDelete = true,
                UsePUT = true
            };
            var result = validator.Validate(entry);

            Assert.False(result.IsValid);
            Assert.Contains("Use Delete", result.Errors[0].ErrorMessage);
        }
    }
}
