namespace AWSRedrive.Models
{
    public class AppSettings
    {
        public DashboardSettings Dashboard { get; set; } = new DashboardSettings();
        public MetricsSettings Metrics { get; set; } = new MetricsSettings();
    }

    public class DashboardSettings
    {
        public bool Enabled { get; set; } = false;
        public int Port { get; set; } = 5000;
        public int RefreshIntervalMs { get; set; } = 5000;
    }

    public class MetricsSettings
    {
        public bool Enabled { get; set; } = false;
        public int IntervalSeconds { get; set; } = 300;
    }
}
