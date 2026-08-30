using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

/// <summary>
/// SQS in, Kafka out. The sink consumes the test topics and records what lands
/// there, so these assertions read the same way as the HTTP ones.
/// See KafkaMessageProcessor.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class KafkaSinkTests : IntegrationTest
{
    [Fact]
    public async Task AnSqsMessageReachesTheConfiguredKafkaTopicIntact()
    {
        var correlation = NewCorrelation();
        var body = JsonBody(correlation, new { payload = "kafka-plain" });

        await Queues.SendAsync("it-kafka-plain", body);

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("kafka", record.Transport);
        Assert.Equal("it-kafka-plain", record.Topic);
        Assert.Equal(body, record.Body);
    }

    [Fact]
    public async Task CompressionEnabledStillDeliversAnIntactPayload()
    {
        var correlation = NewCorrelation();

        // Repetitive content, so Snappy actually has something to compress and the
        // round trip is a real one rather than a pass-through.
        var payload = string.Concat(Enumerable.Repeat("compress-me-", 2000));
        var body = JsonBody(correlation, new { payload });

        await Queues.SendAsync("it-kafka-compressed", body);

        var records = await Sink.WaitForDeliveryAsync(correlation);

        var record = Assert.Single(records);
        Assert.Equal("kafka", record.Transport);
        Assert.Equal("it-kafka-compressed", record.Topic);
        Assert.Equal(body, record.Body);
    }

    [Fact]
    public async Task SqsMessageAttributesAreNotCarriedToKafka()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-kafka-plain", JsonBody(correlation), new Dictionary<string, string>
        {
            ["X-Trace-Id"] = "should-not-survive"
        });

        var records = await Sink.WaitForDeliveryAsync(correlation);
        var record = Assert.Single(records);

        // KafkaMessageProcessor.ProcessMessage takes an attributes argument and
        // ignores it - it produces a Message<Null, string> with no headers at all.
        // Pinned here so that changing it has to be a deliberate act.
        Assert.Null(record.Header("X-Trace-Id"));
        Assert.Empty(record.Headers);
    }
}
