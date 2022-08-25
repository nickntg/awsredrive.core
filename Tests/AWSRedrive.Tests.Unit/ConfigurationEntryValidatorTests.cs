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
        public void NoRedriveUrl()
        {
            var validator = new ConfigurationEntryValidator();
            var entry = new ConfigurationEntry
            {
                QueueUrl = "queue url"
            };
            var result = validator.Validate(entry);

            Assert.False(result.IsValid);
            Assert.Contains("Redrive Url", result.Errors[0].ErrorMessage);
        }

        [Fact]
        public void NoRedriveScript()
        {
            var validator = new ConfigurationEntryValidator();
            var entry = new ConfigurationEntry
            {
                QueueUrl = "queue url",
                RedriveKafkaTopic = "kafka topic"
            };
            var result = validator.Validate(entry);

            Assert.False(result.IsValid);
            Assert.Contains("Redrive Script", result.Errors[0].ErrorMessage);
        }

        [Fact]
        public void NoRedriveKafkaTopic()
        {
            var validator = new ConfigurationEntryValidator();
            var entry = new ConfigurationEntry
            {
                QueueUrl = "queue url",
                RedriveScript = "redrive script"
            };
            var result = validator.Validate(entry);

            Assert.False(result.IsValid);
            Assert.Contains("Redrive Kafka", result.Errors[0].ErrorMessage);
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
