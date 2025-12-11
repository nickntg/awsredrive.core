using System;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class MetricsStoreTests
    {
        [Fact]
        public void GetOrCreate_NewAlias_CreatesMetrics()
        {
            var alias = "test-" + Guid.NewGuid();

            var metrics = MetricsStore.GetOrCreate(alias);

            Assert.NotNull(metrics);
            Assert.Equal(alias, metrics.Alias);
            Assert.Equal(0, metrics.MessagesReceived);
            Assert.Equal(0, metrics.MessagesSent);
            Assert.Equal(0, metrics.MessagesFailed);
        }

        [Fact]
        public void GetOrCreate_ExistingAlias_ReturnsSameInstance()
        {
            var alias = "test-" + Guid.NewGuid();

            var metrics1 = MetricsStore.GetOrCreate(alias);
            metrics1.MessagesReceived = 10;
            var metrics2 = MetricsStore.GetOrCreate(alias);

            Assert.Same(metrics1, metrics2);
            Assert.Equal(10, metrics2.MessagesReceived);
        }

        [Fact]
        public void GetAll_ReturnsAllMetrics()
        {
            var alias1 = "test-" + Guid.NewGuid();
            var alias2 = "test-" + Guid.NewGuid();
            MetricsStore.GetOrCreate(alias1);
            MetricsStore.GetOrCreate(alias2);

            var all = MetricsStore.GetAll();

            Assert.True(all.ContainsKey(alias1));
            Assert.True(all.ContainsKey(alias2));
        }

        [Fact]
        public void Remove_ExistingAlias_RemovesMetrics()
        {
            var alias = "test-" + Guid.NewGuid();
            MetricsStore.GetOrCreate(alias);

            MetricsStore.Remove(alias);

            var all = MetricsStore.GetAll();
            Assert.False(all.ContainsKey(alias));
        }

        [Fact]
        public void Remove_NonExistingAlias_DoesNotThrow()
        {
            var alias = "non-existing-" + Guid.NewGuid();

            var exception = Record.Exception(() => MetricsStore.Remove(alias));

            Assert.Null(exception);
        }

        [Fact]
        public void Metrics_CanBeUpdated()
        {
            var alias = "test-" + Guid.NewGuid();
            var metrics = MetricsStore.GetOrCreate(alias);

            metrics.MessagesReceived = 100;
            metrics.MessagesSent = 95;
            metrics.MessagesFailed = 5;
            metrics.LastMessageReceived = DateTime.UtcNow;
            metrics.LastMessageSent = DateTime.UtcNow;
            metrics.LastError = DateTime.UtcNow;
            metrics.LastErrorMessage = "Test error";
            metrics.StartedAt = DateTime.UtcNow;

            var retrieved = MetricsStore.GetOrCreate(alias);

            Assert.Equal(100, retrieved.MessagesReceived);
            Assert.Equal(95, retrieved.MessagesSent);
            Assert.Equal(5, retrieved.MessagesFailed);
            Assert.Equal("Test error", retrieved.LastErrorMessage);
        }
    }
}
