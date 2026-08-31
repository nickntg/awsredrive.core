using Xunit;

namespace AWSRedrive.Test.Integration.Infrastructure;

/// <summary>
/// Probes the stack once per run. Without this, a forgotten start.ps1 produces
/// a screenful of socket timeouts instead of one sentence saying what to do.
/// </summary>
public sealed class StackPreflight : IAsyncLifetime
{
    public TestEnvironment Environment { get; } = TestEnvironment.Instance;

    public async Task InitializeAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        var probes = new (string Name, string Url)[]
        {
            ("sink", $"{Environment.SinkHttpUrl.TrimEnd('/')}/health"),
            ("redrive dashboard", $"{Environment.DashboardUrl.TrimEnd('/')}/health")
        };

        var unreachable = new List<string>();

        foreach (var (name, url) in probes)
        {
            try
            {
                var response = await http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    unreachable.Add($"{name} ({url} returned {(int)response.StatusCode})");
                }
            }
            catch (Exception ex)
            {
                unreachable.Add($"{name} ({url}: {ex.GetBaseException().Message})");
            }
        }

        using var queues = new TestQueueClient(Environment);
        try
        {
            await queues.ApproximateDepthAsync("it-http-post");
        }
        catch (Exception ex)
        {
            unreachable.Add($"Floci SQS ({Environment.FlociUrl}: {ex.GetBaseException().Message})");
        }

        if (unreachable.Count > 0)
        {
            throw new InvalidOperationException(
                "The integration stack is not running. Start it first:" + System.Environment.NewLine +
                "    cd integration-infrastructure" + System.Environment.NewLine +
                "    ./start.ps1" + System.Environment.NewLine + System.Environment.NewLine +
                "Unreachable: " + string.Join("; ", unreachable));
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Every integration test joins this collection, so the preflight runs once and
/// the tests within it run sequentially against the shared stack.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<StackPreflight>
{
    public const string Name = "integration";
}
