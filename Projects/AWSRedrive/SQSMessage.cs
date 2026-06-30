using System.Collections.Generic;
using AWSRedrive.Interfaces;

namespace AWSRedrive
{
    public class SqsMessage : IMessage
    {
        public string MessageId { get; }
        public string ReceiptHandle { get; }
        public string Content { get; }
        public Dictionary<string, string> Attributes { get; set; }

        public SqsMessage(string messageId, string receiptHandle, string content, Dictionary<string, string> attributes)
        {
            MessageId = messageId;
            ReceiptHandle = receiptHandle;
            Content = content;
            Attributes = attributes;
        }
    }
}