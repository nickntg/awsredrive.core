using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AWSRedrive.Test.Integration.Infrastructure;

/// <summary>
/// Reads back what the sink recorded, and drives its admin endpoint.
/// </summary>
public sealed class SinkClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public SinkClient(TestEnvironment? environment = null)
    {
        var env = environment ?? TestEnvironment.Instance;
        _baseUrl = env.SinkHttpUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<IReadOnlyList<RecordedRequest>> GetRecordedAsync(string correlationId)
    {
        var json = await _http.GetStringAsync($"{_baseUrl}/recorded/{correlationId}");
        return JsonSerializer.Deserialize<List<RecordedRequest>>(json) ?? new List<RecordedRequest>();
    }

    /// <summary>
    /// Polls until at least <paramref name="minimumCount"/> deliveries carrying
    /// this correlation id have been recorded. Throws with a readable message on
    /// timeout rather than letting the caller assert on an empty list.
    /// </summary>
    public async Task<IReadOnlyList<RecordedRequest>> WaitForDeliveryAsync(
        string correlationId,
        TimeSpan? timeout = null,
        int minimumCount = 1)
    {
        var budget = timeout ?? DefaultTimeout;
        var deadline = DateTime.UtcNow + budget;

        IReadOnlyList<RecordedRequest> records = Array.Empty<RecordedRequest>();

        while (DateTime.UtcNow < deadline)
        {
            records = await GetRecordedAsync(correlationId);
            if (records.Count >= minimumCount)
            {
                return records;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"Expected at least {minimumCount} delivery/deliveries for correlation " +
            $"'{correlationId}' within {budget.TotalSeconds:0}s, but saw {records.Count}. " +
            "Check `docker compose logs redrive` in integration-infrastructure/.");
    }

    /// <summary>
    /// Asserts that nothing arrives for this correlation id within the window.
    /// Used by the negative tests - an inactive alias, a rejected certificate.
    /// </summary>
    public async Task AssertNoDeliveryAsync(string correlationId, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;

        while (DateTime.UtcNow < deadline)
        {
            var records = await GetRecordedAsync(correlationId);
            if (records.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Expected no delivery for correlation '{correlationId}', but the sink " +
                    $"recorded {records.Count}. First: {records[0]}");
            }

            await Task.Delay(PollInterval);
        }
    }

    public async Task<JsonArray> GetConfigAsync()
    {
        var json = await _http.GetStringAsync($"{_baseUrl}/admin/redrive-config");
        return JsonNode.Parse(json)!.AsArray();
    }

    public async Task SetConfigAsync(JsonArray entries)
    {
        var content = new StringContent(entries.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{_baseUrl}/admin/redrive-config", content);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Restores the configuration baked into the sink image at build time.</summary>
    public async Task ResetConfigAsync()
    {
        var response = await _http.DeleteAsync($"{_baseUrl}/admin/redrive-config");
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
