namespace AWSRedrive.Interfaces
{
    public interface IMetricsSettings
    {
        bool Enabled { get; }
        int IntervalSeconds { get; }
    }
}
