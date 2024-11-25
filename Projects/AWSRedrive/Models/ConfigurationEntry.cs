namespace AWSRedrive.Models
{
    public class ConfigurationEntry
    {
        public string Alias { get; set; }
        public string Profile { get; set; }
        public string AccessKey { get; set; }
        public string SecretKey { get; set; }
        public string QueueUrl { get; set; }
        public string Region { get; set; }
        public string RedriveUrl { get; set; }
        public string RedriveScript { get; set; }
        public string RedriveKafkaTopic { get; set; }
        public string KafkaBootstrapServers { get; set; }
        public string KafkaClientId { get; set; }
        public bool UseKafkaCompression { get; set; }
        public string AwsGatewayToken { get; set; }
        public string AuthToken { get; set; }
        public string BasicAuthUserName { get; set; }
        public string BasicAuthPassword { get; set; }
        public bool Active { get; set; }
        public bool UsePut { get; set; }
        public bool UseGet { get; set; }
        public bool UseDelete { get; set; }
        public int? Timeout { get; set; }
        public bool IgnoreCertificateErrors { get; set; }
        public bool UnpackAttributesAsHeaders { get; set; }
        public string ServiceUrl { get; set; }
    }
}