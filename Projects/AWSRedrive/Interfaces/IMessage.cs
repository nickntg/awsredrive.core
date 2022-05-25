using System.Collections.Generic;

namespace AWSRedrive.Interfaces
{
    public interface IMessage
    {
        string MessageIdentifier { get; }
        string Content { get; }
        Dictionary<string, string> Attributes { get; }
    }
}