using AWSRedrive.Interfaces;

namespace AWSRedrive
{
    public class SqsMessage : IMessage
    {
        public string MessageIdentifier { get; }
        public string Content { get; }

        public SqsMessage(string identifier, string content)
        {
            MessageIdentifier = identifier;
            Content = content;
        }
    }
}