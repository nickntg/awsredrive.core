using System.Collections.Concurrent;
using AWSRedrive.Models;

namespace AWSRedrive
{
    public static class MetricsStore
    {
        private static readonly ConcurrentDictionary<string, QueueMetrics> _metrics = new();

        public static QueueMetrics GetOrCreate(string alias)
        {
            return _metrics.GetOrAdd(alias, _ => new QueueMetrics { Alias = alias });
        }

        public static ConcurrentDictionary<string, QueueMetrics> GetAll()
        {
            return _metrics;
        }

        public static void Remove(string alias)
        {
            _metrics.TryRemove(alias, out _);
        }
    }
}
