using System.Text.Json;
using System.Text.Json.Nodes;

namespace AWSRedrive.Test.Integration.Infrastructure;

/// <summary>
/// Shared plumbing for every integration test: a sink client, an SQS client, and
/// the correlation id that keeps concurrent tests from seeing each other's
/// traffic. Aliases are shared between tests - the correlation id is what makes
/// each test's messages findable.
/// </summary>
public abstract class IntegrationTest : IDisposable
{
    protected SinkClient Sink { get; } = new();

    protected TestQueueClient Queues { get; } = new();

    protected TestEnvironment Env { get; } = TestEnvironment.Instance;

    /// <summary>A fresh correlation id. One per message, never reused.</summary>
    protected static string NewCorrelation() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// A minimal JSON body carrying the correlation id. The sink pulls the id out
    /// of the body, so any message built this way is findable.
    /// </summary>
    protected static string JsonBody(string correlation) =>
        JsonSerializer.Serialize(new { correlation });

    protected static string JsonBody(string correlation, object extra)
    {
        var node = JsonSerializer.SerializeToNode(extra)!.AsObject();
        node["correlation"] = correlation;
        return node.ToJsonString();
    }

    public virtual void Dispose()
    {
        Queues.Dispose();
        GC.SuppressFinalize(this);
    }
}
