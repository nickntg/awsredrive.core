using System.Text.Json.Nodes;
using AWSRedrive.Test.Integration.Infrastructure;
using Xunit;

namespace AWSRedrive.Test.Integration;

/// <summary>
/// Orchestrator.StartProcessing re-reads config.json every 60 seconds and
/// ConfigurationChangeManager reconciles the running processors against it.
/// These tests change the configuration through the sink's admin endpoint and
/// wait for that loop to come around, so they are the slowest in the suite.
///
/// Each test owns an alias nothing else touches - it-inactive, it-activatable,
/// it-reload, it-removable. Restoring the baseline in teardown does not take
/// effect for up to another 60 seconds, so a test that mutated a shared alias
/// would leave the next one running against a configuration redrive has not
/// caught up with yet.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Speed", "Slow")]
public class ConfigurationReloadTests : IntegrationTest, IAsyncLifetime
{
    private static readonly TimeSpan ReloadBudget = TimeSpan.FromSeconds(90);

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Puts the committed configuration back, even after a failure.</summary>
    public Task DisposeAsync() => Sink.ResetConfigAsync();

    private static JsonObject FindAlias(JsonArray config, string alias) =>
        config.OfType<JsonObject>().Single(e => (string?)e["Alias"] == alias);

    [Fact]
    public async Task AnInactiveAliasNeverDrainsItsQueue()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-inactive", JsonBody(correlation));

        // it-inactive is Active:false in the committed config, so
        // FindConfigsToAdd skips it and no processor is ever created.
        await Sink.AssertNoDeliveryAsync(correlation, TimeSpan.FromSeconds(30));
        Assert.True(await Queues.ApproximateDepthAsync("it-inactive") > 0);
    }

    [Fact]
    public async Task ActivatingAnAliasAtRuntimeDrainsItsQueue()
    {
        var correlation = NewCorrelation();

        // it-activatable, not it-inactive: this test leaves the alias active until
        // the baseline restore reconciles, which would break the test above.
        await Queues.SendAsync("it-activatable", JsonBody(correlation));

        var config = await Sink.GetConfigAsync();
        FindAlias(config, "it-activatable")["Active"] = true;
        await Sink.SetConfigAsync(config);

        var records = await Sink.WaitForDeliveryAsync(correlation, ReloadBudget);

        Assert.Equal("/http/activatable", records[0].Path);
    }

    [Fact]
    public async Task AnAliasAddedAtRuntimeStartsDelivering()
    {
        var correlation = NewCorrelation();

        await Queues.SendAsync("it-reload", JsonBody(correlation));

        // it-reload has a queue but no alias in the committed config, so nothing
        // is polling it yet.
        await Sink.AssertNoDeliveryAsync(correlation, TimeSpan.FromSeconds(10));

        var config = await Sink.GetConfigAsync();
        config.Add(new JsonObject
        {
            ["Alias"] = "it-reload",
            ["QueueUrl"] = "http://floci:4566/000000000000/it-reload",
            ["ServiceUrl"] = "http://floci:4566",
            ["Region"] = "us-east-1",
            ["AccessKey"] = "test",
            ["SecretKey"] = "test",
            ["RedriveUrl"] = "http://sink:8080/http/reload",
            ["Active"] = true,
            ["Timeout"] = 10000,
            ["LogLevel"] = "Debug"
        });
        await Sink.SetConfigAsync(config);

        var records = await Sink.WaitForDeliveryAsync(correlation, ReloadBudget);

        Assert.Equal("/http/reload", records[0].Path);
    }

    [Fact]
    public async Task RemovingAnAliasStopsItsProcessor()
    {
        // it-removable exists only for this test - removing an alias that another
        // test uses would break it for the up-to-60s the restore takes to land.
        //
        // Prove the alias is live before removing it, so a failure below cannot be
        // mistaken for the alias having been broken all along.
        var before = NewCorrelation();
        await Queues.SendAsync("it-removable", JsonBody(before));
        await Sink.WaitForDeliveryAsync(before);

        var config = await Sink.GetConfigAsync();
        var remaining = new JsonArray();
        foreach (var entry in config.OfType<JsonObject>())
        {
            if ((string?)entry["Alias"] == "it-removable")
            {
                continue;
            }

            remaining.Add(JsonNode.Parse(entry.ToJsonString())!);
        }

        await Sink.SetConfigAsync(remaining);

        // Give the reconciliation loop a full cycle to stop the processor.
        await Task.Delay(TimeSpan.FromSeconds(75));

        var after = NewCorrelation();
        await Queues.SendAsync("it-removable", JsonBody(after));
        await Sink.AssertNoDeliveryAsync(after, TimeSpan.FromSeconds(30));
    }
}
