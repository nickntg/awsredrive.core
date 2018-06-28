namespace AWSRedrive
{
    public class ConfigurationEntry
    {
        public string Alias { get; set; }
        public string AccessKey { get; set; }
        public string SecretKey { get; set; }
        public string QueueUrl { get; set; }
        public string Region { get; set; }
        public string RedriveUrl { get; set; }
        public string AwsGatewayToken { get; set; }
        public bool Active { get; set; }
    }
}