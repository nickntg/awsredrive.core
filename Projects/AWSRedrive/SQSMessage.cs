using System.Collections.Generic;
using AWSRedrive.Interfaces;

namespace AWSRedrive
{
    public class SqsMessage(string identifier, string content, Dictionary<string, string> attributes)
        : IMessage
    {
        public string MessageIdentifier { get; } = identifier;
        public string Content { get; } = content;
        public Dictionary<string, string> Attributes { get; set; } = attributes;
    }
}