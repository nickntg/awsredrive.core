using Amazon.SQS;
using Amazon.SQS.Model;

namespace AWSRedrive.Test.Integration.Infrastructure;

/// <summary>
/// Sends messages into Floci and inspects queues from the test process. This is
/// the test side of the conversation; redrive has its own SQS client inside the
/// container network.
/// </summary>
public sealed class TestQueueClient : IDisposable
{
    private readonly TestEnvironment _env;
    private readonly IAmazonSQS _sqs;

    public TestQueueClient(TestEnvironment? environment = null)
    {
        _env = environment ?? TestEnvironment.Instance;
        _sqs = _env.CreateSqsClient();
    }

    public async Task SendAsync(
        string queueName,
        string body,
        IDictionary<string, string>? attributes = null)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = _env.QueueUrl(queueName),
            MessageBody = body
        };

        if (attributes is { Count: > 0 })
        {
            request.MessageAttributes = attributes.ToDictionary(
                pair => pair.Key,
                pair => new MessageAttributeValue { DataType = "String", StringValue = pair.Value });
        }

        await _sqs.SendMessageAsync(request);
    }

    public async Task<int> ApproximateDepthAsync(string queueName)
    {
        var response = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = _env.QueueUrl(queueName),
            AttributeNames = new List<string> { "ApproximateNumberOfMessages" }
        });

        return response.Attributes is not null
            && response.Attributes.TryGetValue("ApproximateNumberOfMessages", out var value)
            && int.TryParse(value, out var count)
                ? count
                : 0;
    }

    public async Task<string?> GetRedrivePolicyAsync(string queueName)
    {
        var response = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = _env.QueueUrl(queueName),
            AttributeNames = new List<string> { "RedrivePolicy" }
        });

        return response.Attributes is not null
            && response.Attributes.TryGetValue("RedrivePolicy", out var value)
                ? value
                : null;
    }

    /// <summary>
    /// Polls a dead letter queue until a message whose body contains the
    /// correlation id shows up. Messages are left on the queue - the short
    /// visibility timeout means a concurrent test can still find its own.
    /// </summary>
    public async Task<Message?> WaitForDlqMessageAsync(
        string dlqName,
        string correlationId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = _env.QueueUrl(dlqName),
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 2,
                VisibilityTimeout = 1
            });

            var match = response.Messages?.FirstOrDefault(
                m => m.Body is not null && m.Body.Contains(correlationId, StringComparison.Ordinal));

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    public async Task PurgeAsync(string queueName)
    {
        try
        {
            await _sqs.PurgeQueueAsync(new PurgeQueueRequest { QueueUrl = _env.QueueUrl(queueName) });
        }
        catch (AmazonSQSException)
        {
            // PurgeQueue is rate limited to once every 60s per queue on real SQS,
            // and Floci may not implement it at all. Neither is worth failing a
            // test over - correlation ids already keep tests from colliding.
        }
    }

    public void Dispose() => _sqs.Dispose();
}
