using Amazon.SQS;
using Amazon.SQS.Model;
using LiteBus.Transport.IntegrationTesting;

namespace LiteBus.Transport.IntegrationTesting.Aws;

/// <summary>
///     SQS-specific helpers for durable transport integration tests.
/// </summary>
public static class SqsTransportTestInfrastructure
{
    /// <summary>
    ///     Receives one message from the supplied queue URL.
    /// </summary>
    /// <param name="sqsClient">The SQS client bound to LocalStack.</param>
    /// <param name="queueUrl">The queue URL to poll.</param>
    /// <param name="timeout">The maximum time to wait for a message.</param>
    /// <returns>The message body and mapped transport headers.</returns>
    public static async Task<(string Body, IReadOnlyDictionary<string, object?> Headers)> ReceiveOneAsync(
        IAmazonSQS sqsClient,
        string queueUrl,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(sqsClient);

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var response = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = 1,
                WaitTimeSeconds = 1,
                MessageAttributeNames = ["All"]
            }).ConfigureAwait(false);

            if (response.Messages.Count == 0)
            {
                continue;
            }

            var message = response.Messages[0];
            var headers = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var attribute in message.MessageAttributes)
            {
                headers[attribute.Key] = attribute.Value.StringValue;
            }

            return (message.Body, headers);
        }

        throw new TimeoutException($"No SQS message was received from '{queueUrl}' within {timeout}.");
    }

    /// <summary>
    ///     Waits until the supplied queue reports the expected approximate message count.
    /// </summary>
    /// <param name="sqsClient">The SQS client bound to LocalStack.</param>
    /// <param name="queueUrl">The queue URL to inspect.</param>
    /// <param name="expectedCount">The expected approximate message count.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the queue depth matches.</returns>
    public static async Task WaitForQueueDepthAsync(
        IAmazonSQS sqsClient,
        string queueUrl,
        int expectedCount,
        TimeSpan timeout)
    {
        await PollingWait.UntilAsync(async () =>
        {
            var attributes = await sqsClient
                .GetQueueAttributesAsync(queueUrl, ["ApproximateNumberOfMessages"]).ConfigureAwait(false);

            return attributes.ApproximateNumberOfMessages == expectedCount;
        }, timeout).ConfigureAwait(false);
    }
}