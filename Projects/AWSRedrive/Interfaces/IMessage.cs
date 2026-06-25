using System.Collections.Generic;

namespace AWSRedrive.Interfaces
{
    public interface IMessage
    {
        /// <summary>
        /// SQS Message ID - short UUID for logging and correlation
        /// </summary>
        string MessageId { get; }
        
        /// <summary>
        /// SQS Receipt Handle - long base64 string required for delete operations
        /// </summary>
        string ReceiptHandle { get; }
        
        string Content { get; }
        Dictionary<string, string> Attributes { get; }
    }
}