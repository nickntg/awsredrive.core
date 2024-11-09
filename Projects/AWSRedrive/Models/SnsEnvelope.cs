using System.Collections.Generic;

namespace AWSRedrive.Models
{
    public class SnsEnvelope
    {
        public Dictionary<string, MessageAttribute> MessageAttributes { get; set; }
    }
}
