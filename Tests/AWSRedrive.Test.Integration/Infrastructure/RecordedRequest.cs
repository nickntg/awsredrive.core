using System.Text.Json.Serialization;

namespace AWSRedrive.Test.Integration.Infrastructure;

/// <summary>
/// One delivery observed by the sink, either an HTTP request or a message read
/// off a Kafka topic. <see cref="Method"/>, <see cref="Path"/> and
/// <see cref="Query"/> are null or empty for Kafka; <see cref="Topic"/> is null
/// for HTTP.
/// </summary>
public sealed class RecordedRequest
{
    [JsonPropertyName("transport")] public string Transport { get; set; } = "";

    [JsonPropertyName("method")] public string? Method { get; set; }

    [JsonPropertyName("path")] public string? Path { get; set; }

    [JsonPropertyName("query")] public Dictionary<string, string> Query { get; set; } = new();

    [JsonPropertyName("headers")] public Dictionary<string, string> Headers { get; set; } = new();

    [JsonPropertyName("body")] public string? Body { get; set; }

    [JsonPropertyName("topic")] public string? Topic { get; set; }

    [JsonPropertyName("at")] public string? At { get; set; }

    [JsonPropertyName("correlation")] public string? Correlation { get; set; }

    /// <summary>Header lookup by name, case-insensitive. Null when absent.</summary>
    public string? Header(string name)
    {
        foreach (var pair in Headers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    public bool HasHeader(string name) => Header(name) is not null;

    public override string ToString() =>
        Transport == "kafka"
            ? $"kafka topic={Topic} at={At}"
            : $"{Method} {Path} at={At}";
}
