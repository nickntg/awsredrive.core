using System.Collections.Generic;
using AWSRedrive.Interfaces;

namespace AWSRedrive
{
    public class SqsMessage : IMessage
    {
        public string MessageIdentifier { get; }
        public string Content { get; }
        public Dictionary<string, string> Attributes { get; set; }

        public SqsMessage(string identifier, string content, Dictionary<string, string> attributes)
        {
            MessageIdentifier = identifier;
            Content = content;
            Attributes = attributes;
        }
    }
}