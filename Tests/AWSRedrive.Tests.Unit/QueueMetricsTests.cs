using System;
using AWSRedrive.Models;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class QueueMetricsTests
    {
        [Fact]
        public void NewInstance_HasDefaultValues()
        {
            var metrics = new QueueMetrics();

            Assert.Null(metrics.Alias);
            Assert.Equal(0, metrics.MessagesReceived);
            Assert.Equal(0, metrics.MessagesSent);
            Assert.Equal(0, metrics.MessagesFailed);
            Assert.Null(metrics.LastMessageReceived);
            Assert.Null(metrics.LastMessageSent);
            Assert.Null(metrics.LastError);
            Assert.Null(metrics.LastErrorMessage);
            Assert.Null(metrics.LastMessageContent);
            Assert.Equal(default(DateTime), metrics.StartedAt);
        }

        [Fact]
        public void Properties_CanBeSet()
        {
            var now = DateTime.UtcNow;
            var metrics = new QueueMetrics
            {
                Alias = "test-alias",
                MessagesReceived = 100,
                MessagesSent = 90,
                MessagesFailed = 10,
                LastMessageReceived = now,
                LastMessageSent = now,
                LastError = now,
                LastErrorMessage = "Error message",
                LastMessageContent = "{\"test\":\"content\"}",
                StartedAt = now
            };

            Assert.Equal("test-alias", metrics.Alias);
            Assert.Equal(100, metrics.MessagesReceived);
            Assert.Equal(90, metrics.MessagesSent);
            Assert.Equal(10, metrics.MessagesFailed);
            Assert.Equal(now, metrics.LastMessageReceived);
            Assert.Equal(now, metrics.LastMessageSent);
            Assert.Equal(now, metrics.LastError);
            Assert.Equal("Error message", metrics.LastErrorMessage);
            Assert.Equal("{\"test\":\"content\"}", metrics.LastMessageContent);
            Assert.Equal(now, metrics.StartedAt);
        }
    }
}
