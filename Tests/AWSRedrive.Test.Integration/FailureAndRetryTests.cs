using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

/// <summary>
/// What happens when the destination misbehaves. QueueProcessor only deletes a
/// message after the processor returns without throwing, so a failed delivery
/// must come back around.
///
/// The queues these tests use have a 5 second visibility timeout, so a rejected
/// message reappears quickly instead of after the SQS default of thirty.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Speed", "Slow")]
public class FailureAndRetryTests : IntegrationTest
{
    [Fact]
    public async Task ASinkThatStallsPastTheTimeoutCausesRedelivery()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-timeout", JsonBody(correlation));

        // The alias has Timeout=2000 against an endpoint that sleeps 6000ms, so
        // redrive abandons every attempt and never deletes the message.
        var records = await Sink.WaitForDeliveryAsync(
            correlation, TimeSpan.FromSeconds(60), minimumCount: 2);

        Assert.True(records.Count >= 2, $"Expected at least 2 attempts, saw {records.Count}.");
        Assert.All(records, r => Assert.Equal("/http/slow", r.Path));
    }

    [Fact]
    public async Task ASinkReturning500CausesRedelivery()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-failing", JsonBody(correlation));

        var records = await Sink.WaitForDeliveryAsync(
            correlation, TimeSpan.FromSeconds(60), minimumCount: 2);

        Assert.True(records.Count >= 2, $"Expected at least 2 attempts, saw {records.Count}.");
        Assert.All(records, r => Assert.Equal("/http/fail", r.Path));
    }

    [Fact]
    public async Task AMessageRejectedPastMaxReceiveCountLandsInTheDeadLetterQueue()
    {
        var correlation = NewCorrelation();

        // Guard: without a RedrivePolicy this can never pass, and the failure
        // should say why rather than just timing out sixty seconds later.
        var policy = await Queues.GetRedrivePolicyAsync("it-failing");
        Assert.False(string.IsNullOrEmpty(policy),
            "it-failing has no RedrivePolicy. Floci may not support DLQ redrive - " +
            "see Known Risks in docs/superpowers/specs/2026-08-30-integration-test-suite-design.md.");

        await Queues.SendAsync("it-failing", JsonBody(correlation));

        // maxReceiveCount=2 with a 5s visibility timeout: two attempts, then the DLQ.
        var message = await Queues.WaitForDlqMessageAsync(
            "it-failing-dlq", correlation, TimeSpan.FromSeconds(90));

        Assert.NotNull(message);
        Assert.Contains(correlation, message!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASuccessfullyDeliveredMessageIsRemovedFromItsQueue()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-http-post", JsonBody(correlation));
        await Sink.WaitForDeliveryAsync(correlation);

        // Delivery happened. The queue must drain and stay drained - a message
        // that was never deleted would reappear once its visibility timeout
        // expired, and be delivered a second time.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        var depth = -1;

        while (DateTime.UtcNow < deadline)
        {
            depth = await Queues.ApproximateDepthAsync("it-http-post");
            if (depth == 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, depth);

        var records = await Sink.GetRecordedAsync(correlation);
        Assert.Single(records);
    }
}
