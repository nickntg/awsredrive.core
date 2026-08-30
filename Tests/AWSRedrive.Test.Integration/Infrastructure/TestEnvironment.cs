using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;

namespace AWSRedrive.Test.Integration.Infrastructure;

/// <summary>
/// Endpoints and credentials for the stack started by
/// integration-infrastructure/start.ps1. Every value can be overridden with an
/// environment variable named AWSREDRIVE_IT_&lt;PROPERTY&gt;, so a developer who
/// has remapped a port does not have to edit a committed file.
/// </summary>
public sealed class TestEnvironment
{
    private const string EnvPrefix = "AWSREDRIVE_IT_";

    public static TestEnvironment Instance { get; } = Load();

    public string SinkHttpUrl { get; init; } = "http://localhost:8080";
    public string SinkHttpsUrl { get; init; } = "https://localhost:8443";
    public string DashboardUrl { get; init; } = "http://localhost:5000";
    public string FlociUrl { get; init; } = "http://localhost:4566";
    public string AccountId { get; init; } = "000000000000";
    public string Region { get; init; } = "us-east-1";
    public string AccessKey { get; init; } = "test";
    public string SecretKey { get; init; } = "test";
    public string ExpectedAuthToken { get; init; } = "Bearer it-integration-token";
    public string ExpectedApiKey { get; init; } = "it-integration-api-key";
    public string ExpectedBasicUser { get; init; } = "it-user";
    public string ExpectedBasicPassword { get; init; } = "it-password";

    private static TestEnvironment Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "integrationsettings.json");

        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<TestEnvironment>(
                  File.ReadAllText(path),
                  new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
              ?? new TestEnvironment()
            : new TestEnvironment();

        return new TestEnvironment
        {
            SinkHttpUrl = Override(nameof(SinkHttpUrl), settings.SinkHttpUrl),
            SinkHttpsUrl = Override(nameof(SinkHttpsUrl), settings.SinkHttpsUrl),
            DashboardUrl = Override(nameof(DashboardUrl), settings.DashboardUrl),
            FlociUrl = Override(nameof(FlociUrl), settings.FlociUrl),
            AccountId = Override(nameof(AccountId), settings.AccountId),
            Region = Override(nameof(Region), settings.Region),
            AccessKey = Override(nameof(AccessKey), settings.AccessKey),
            SecretKey = Override(nameof(SecretKey), settings.SecretKey),
            ExpectedAuthToken = Override(nameof(ExpectedAuthToken), settings.ExpectedAuthToken),
            ExpectedApiKey = Override(nameof(ExpectedApiKey), settings.ExpectedApiKey),
            ExpectedBasicUser = Override(nameof(ExpectedBasicUser), settings.ExpectedBasicUser),
            ExpectedBasicPassword = Override(nameof(ExpectedBasicPassword), settings.ExpectedBasicPassword)
        };
    }

    private static string Override(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(EnvPrefix + name.ToUpperInvariant());
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    /// <summary>
    /// The queue URL as seen from the test process. Redrive uses the same path on
    /// the "floci" hostname instead - both address the same queue, because Floci
    /// routes by path and ignores the host component.
    /// </summary>
    public string QueueUrl(string queueName) => $"{FlociUrl.TrimEnd('/')}/{AccountId}/{queueName}";

    public IAmazonSQS CreateSqsClient()
    {
        var config = new AmazonSQSConfig
        {
            ServiceURL = FlociUrl,
            AuthenticationRegion = Region
        };

        return new AmazonSQSClient(new BasicAWSCredentials(AccessKey, SecretKey), config);
    }
}
