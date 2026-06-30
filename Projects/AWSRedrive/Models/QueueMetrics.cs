using System;

namespace AWSRedrive.Models
{
    public class QueueMetrics
    {
        public string Alias { get; set; }
        public DateTime StartedAt { get; set; }
        public long MessagesReceived { get; set; }
        public long MessagesSent { get; set; }
        public long MessagesFailed { get; set; }
        public DateTime? LastMessageReceived { get; set; }
        public DateTime? LastMessageSent { get; set; }
        public DateTime? LastError { get; set; }
        public string LastErrorMessage { get; set; }
        public string LastMessageContent { get; set; }
        
        public int UptimeSeconds => StartedAt == default 
            ? 0 
            : (int)(DateTime.UtcNow - StartedAt).TotalSeconds;
    }
}
