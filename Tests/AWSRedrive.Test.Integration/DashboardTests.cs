using System.Text.Json;
using System.Text.Json.Serialization;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

/// <summary>
/// The dashboard's view of the same traffic. DashboardServer.GetStatus returns a
/// camelCase array with a nested metrics object, and omits nulls.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DashboardTests : IntegrationTest
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private sealed class StatusEntry
    {
        [JsonPropertyName("alias")] public string Alias { get; set; } = "";
        [JsonPropertyName("queueUrl")] public string? QueueUrl { get; set; }
        [JsonPropertyName("redriveUrl")] public string? RedriveUrl { get; set; }
        [JsonPropertyName("dlqUrl")] public string? DlqUrl { get; set; }
        [JsonPropertyName("active")] public bool Active { get; set; }
        [JsonPropertyName("metrics")] public StatusMetrics Metrics { get; set; } = new();
    }

    private sealed class StatusMetrics
    {
        [JsonPropertyName("messagesReceived")] public long MessagesReceived { get; set; }
        [JsonPropertyName("messagesSent")] public long MessagesSent { get; set; }
        [JsonPropertyName("messagesFailed")] public long MessagesFailed { get; set; }
    }

    private async Task<StatusEntry> GetAliasStatusAsync(string alias)
    {
        var json = await _http.GetStringAsync($"{Env.DashboardUrl.TrimEnd('/')}/api/status");
        var entries = JsonSerializer.Deserialize<List<StatusEntry>>(json)
                      ?? new List<StatusEntry>();

        var entry = entries.SingleOrDefault(e => e.Alias == alias);
        Assert.True(entry is not null,
            $"The dashboard did not report an alias named '{alias}'. It reported: " +
            string.Join(", ", entries.Select(e => e.Alias)));

        return entry!;
    }

    [Fact]
    public async Task StatusReportsMessageCountsClimbingForAnAlias()
    {
        var sentBefore = (await GetAliasStatusAsync("it-http-post")).Metrics.MessagesSent;

        var correlation = NewCorrelation();
        await Queues.SendAsync("it-http-post", JsonBody(correlation));
        await Sink.WaitForDeliveryAsync(correlation);

        // The dashboard reads MetricsStore directly, but the processor increments
        // MessagesSent just after the delivery the sink already recorded, so give
        // it a moment rather than racing it.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var sentAfter = sentBefore;

        while (DateTime.UtcNow < deadline)
        {
            sentAfter = (await GetAliasStatusAsync("it-http-post")).Metrics.MessagesSent;
            if (sentAfter > sentBefore)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        Assert.True(sentAfter > sentBefore,
            $"messagesSent for it-http-post did not increase (before={sentBefore}, after={sentAfter}).");
    }

    [Fact]
    public async Task DlqUrlIsAutoDiscoveredFromTheRedrivePolicy()
    {
        // Guard: the dashboard can only surface a DlqUrl if the queue has a
        // RedrivePolicy for AwsQueueClient.GetDlqUrl to parse.
        var policy = await Queues.GetRedrivePolicyAsync("it-failing");
        Assert.False(string.IsNullOrEmpty(policy),
            "it-failing has no RedrivePolicy. Floci may not support DLQ redrive - " +
            "see Known Risks in docs/superpowers/specs/2026-08-30-integration-test-suite-design.md.");

        var entry = await GetAliasStatusAsync("it-failing");

        Assert.False(string.IsNullOrEmpty(entry.DlqUrl),
            "The dashboard did not surface a DlqUrl for it-failing.");
        Assert.Contains("it-failing-dlq", entry.DlqUrl!, StringComparison.Ordinal);
    }

    public override void Dispose()
    {
        _http.Dispose();
        base.Dispose();
    }
}
