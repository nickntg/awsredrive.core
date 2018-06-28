namespace AWSRedrive.Interfaces
{
    public interface IOrchestrator
    {
        bool IsProcessing { get; }
        void Start();
        void StartProcessing();
        void Stop();
    }
}