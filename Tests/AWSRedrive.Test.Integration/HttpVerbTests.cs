using System.Text.Json;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

/// <summary>
/// The verb selection knobs on a configuration entry, and how the message body
/// survives the trip. See HttpMessageProcessor.CreateRequest.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class HttpVerbTests : IntegrationTest
{
    [Fact]
    public async Task PostIsTheDefaultVerbAndTheBodyArrivesVerbatim()
    {
        var correlation = NewCorrelation();
        var body = JsonBody(correlation, new { hello = "world" });

        await Queues.SendAsync("it-http-post", body);

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("POST", record.Method);
        Assert.Equal("/http/post", record.Path);
        Assert.Equal(body, record.Body);
    }

    [Fact]
    public async Task UsePutSendsAPutWithTheBodyIntact()
    {
        var correlation = NewCorrelation();
        var body = JsonBody(correlation, new { hello = "world" });

        await Queues.SendAsync("it-http-put", body);

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("PUT", record.Method);
        Assert.Equal("/http/put", record.Path);
        Assert.Equal(body, record.Body);
    }

    [Fact]
    public async Task UseDeleteSendsADeleteThatStillCarriesTheBody()
    {
        var correlation = NewCorrelation();
        var body = JsonBody(correlation, new { hello = "world" });

        await Queues.SendAsync("it-http-delete", body);

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("DELETE", record.Method);
        Assert.Equal("/http/delete", record.Path);
        Assert.Equal(body, record.Body);
    }

    [Fact]
    public async Task UseGetUnwrapsTheJsonBodyIntoQueryParameters()
    {
        var correlation = NewCorrelation();
        var body = JsonBody(correlation, new { hello = "world", count = "42" });

        await Queues.SendAsync("it-http-get", body);

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("GET", record.Method);
        Assert.Equal("/http/get", record.Path);
        Assert.Equal("world", record.Query["hello"]);
        Assert.Equal("42", record.Query["count"]);
        Assert.True(string.IsNullOrEmpty(record.Body), "A GET must not carry a body.");
    }

    [Fact]
    public async Task UseGetWithANonJsonBodyStillDeliversWithoutQueryParameters()
    {
        // The correlation id has to ride in a message attribute here: redrive can
        // only turn a *JSON* body into query parameters, and this body is not JSON.
        // AddAttributes turns the attribute into a header, which the sink reads.
        var correlation = NewCorrelation();

        await Queues.SendAsync(
            "it-http-get",
            "this is not json at all",
            new Dictionary<string, string> { ["X-Correlation-Id"] = correlation });

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("GET", record.Method);
        Assert.Equal("/http/get", record.Path);
        Assert.False(record.Query.ContainsKey("hello"));
    }

    [Fact]
    public async Task ALargeUnicodePayloadArrivesIntact()
    {
        var correlation = NewCorrelation();

        // ~200KB, comfortably under the 256KB SQS limit, with multi-byte
        // characters that would expose any byte-vs-char length confusion on the
        // way through. QueueProcessor truncates at 100KB for *metrics* only, so
        // the delivered body must still be whole.
        var filler = string.Concat(Enumerable.Repeat("αβγδε-日本語-🚀-", 8000));
        var body = JsonBody(correlation, new { filler });

        await Queues.SendAsync("it-http-post", body);

        var records = await Sink.WaitForDeliveryAsync(correlation, TimeSpan.FromSeconds(45));

        var record = Assert.Single(records);
        Assert.Equal(body, record.Body);
    }
}
