using System.Text.Json;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

/// <summary>
/// How SQS message attributes and SNS envelope attributes become HTTP headers.
/// See HttpMessageProcessor.AddAttributes and UnpackAttributesAsHeaders.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class HeaderPropagationTests : IntegrationTest
{
    [Fact]
    public async Task SqsMessageAttributesBecomeHttpHeaders()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-http-post", JsonBody(correlation), new Dictionary<string, string>
        {
            ["X-Trace-Id"] = "trace-12345",
            ["X-Tenant"] = "acme"
        });

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("trace-12345", record.Header("X-Trace-Id"));
        Assert.Equal("acme", record.Header("X-Tenant"));
    }

    [Fact]
    public async Task ReservedHeaderNamesSentAsAttributesAreNotPropagated()
    {
        var correlation = NewCorrelation();

        // HttpMessageProcessor._ignoredHeaders is
        // ["content-length", "host", "accept-encoding", "content-type", "accept"].
        // A caller must not be able to rewrite the Host header of an outbound
        // request by naming an SQS message attribute after it.
        await Queues.SendAsync("it-http-post", JsonBody(correlation), new Dictionary<string, string>
        {
            ["Host"] = "evil.example.com",
            ["Content-Type"] = "text/csv",
            ["Accept"] = "application/xml",
            ["Accept-Encoding"] = "br",
            ["Content-Length"] = "999999",
            ["X-Allowed"] = "kept"
        });

        var records = await Sink.WaitForDeliveryAsync(correlation);
        var record = Assert.Single(records);

        // The allowed one proves the attributes were processed at all, so the
        // assertions below are about filtering rather than a no-op.
        Assert.Equal("kept", record.Header("X-Allowed"));

        Assert.NotEqual("evil.example.com", record.Header("Host"));
        Assert.NotEqual("text/csv", record.Header("Content-Type"));
        Assert.NotEqual("application/xml", record.Header("Accept"));
        Assert.NotEqual("br", record.Header("Accept-Encoding"));
        Assert.NotEqual("999999", record.Header("Content-Length"));
    }

    [Fact]
    public async Task UnpackAttributesAsHeadersLiftsSnsMessageAttributes()
    {
        var correlation = NewCorrelation();

        // The shape AWSRedrive.Models.SnsEnvelope binds: MessageAttributes maps a
        // name to an object with a Value property.
        var body = JsonSerializer.Serialize(new
        {
            correlation,
            Type = "Notification",
            Message = "payload",
            MessageAttributes = new Dictionary<string, object>
            {
                ["X-Sns-Trace"] = new { Type = "String", Value = "sns-trace-99" },
                ["X-Sns-Origin"] = new { Type = "String", Value = "orders" }
            }
        });

        await Queues.SendAsync("it-sns-unpack", body);

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/http/sns", record.Path);
        Assert.Equal("sns-trace-99", record.Header("X-Sns-Trace"));
        Assert.Equal("orders", record.Header("X-Sns-Origin"));
    }

    [Fact]
    public async Task UnpackAttributesAsHeadersOnANonSnsBodyStillDelivers()
    {
        var correlation = NewCorrelation();
        var body = JsonBody(correlation, new { note = "no envelope here" });

        await Queues.SendAsync("it-sns-unpack", body);

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("/http/sns", record.Path);
        Assert.Equal(body, record.Body);
    }
}
