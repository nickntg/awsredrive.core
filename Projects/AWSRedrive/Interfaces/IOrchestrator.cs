namespace AWSRedrive.Interfaces
{
    public interface IOrchestrator
    {
        void Start();
        void Stop();
        bool SetLogLevel(string alias, string level);
        string GetLogLevel(string alias);
    }
}
