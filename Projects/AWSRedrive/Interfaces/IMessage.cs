namespace AWSRedrive.Interfaces
{
    public interface IMessage
    {
        string MessageIdentifier { get; }
        string Content { get; }
    }
}