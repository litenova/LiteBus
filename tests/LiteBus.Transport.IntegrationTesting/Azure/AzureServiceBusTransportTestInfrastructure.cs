using Azure.Messaging.ServiceBus;
using LiteBus.Transport.IntegrationTesting;

namespace LiteBus.Transport.IntegrationTesting.Azure;

/// <summary>
///     Azure Service Bus helpers for durable transport integration tests.
/// </summary>
public static class AzureServiceBusTransportTestInfrastructure
{
    /// <summary>
    ///     Receives one message from the supplied queue and completes it so later tests do not observe stale deliveries.
    /// </summary>
    /// <param name="connectionString">The Service Bus connection string.</param>
    /// <param name="queueName">The queue name to read from.</param>
    /// <param name="timeout">The maximum time to wait for a message.</param>
    /// <returns>The message body and mapped transport headers.</returns>
    public static async Task<(string Body, IReadOnlyDictionary<string, object?> Headers)> ReceiveOneAsync(
        string connectionString,
        string queueName,
        TimeSpan timeout)
    {
         var client = new ServiceBusClient(connectionString);
         await using (client.ConfigureAwait(false))
         {
         var receiver = client.CreateReceiver(queueName);
         await using (receiver.ConfigureAwait(false))
         {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

            if (message is null)
            {
                continue;
            }

            var headers = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var property in message.ApplicationProperties)
            {
                headers[property.Key] = property.Value;
            }

            await receiver.CompleteMessageAsync(message).ConfigureAwait(false);

            return (message.Body.ToString(), headers);
        }

        throw new TimeoutException($"No Service Bus message was received from '{queueName}' within {timeout}.");
        }
        }
    }

    /// <summary>
    ///     Gets a required string header value from Azure Service Bus application properties.
    /// </summary>
    /// <param name="headers">The application properties copied from a received message.</param>
    /// <param name="headerName">The header name to read.</param>
    /// <returns>The header value as a string.</returns>
    public static string GetHeader(IReadOnlyDictionary<string, object?> headers, string headerName)
    {
        ArgumentNullException.ThrowIfNull(headers);
        headers.TryGetValue(headerName, out var value);
        return value?.ToString() ?? string.Empty;
    }

    /// <summary>
    ///     Waits until the supplied queue reports the expected active message count.
    /// </summary>
    /// <param name="connectionString">The Service Bus connection string.</param>
    /// <param name="queueName">The queue name to inspect.</param>
    /// <param name="expectedCount">The expected active message count.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the queue depth matches.</returns>
    public static async Task WaitForQueueDepthAsync(
        string connectionString,
        string queueName,
        int expectedCount,
        TimeSpan timeout)
    {
         var client = new ServiceBusClient(connectionString);
         await using (client.ConfigureAwait(false))
         {
         var receiver = client.CreateReceiver(queueName);
         await using (receiver.ConfigureAwait(false))
         {

        await PollingWait.UntilAsync(async () =>
        {
            var count = await receiver.PeekMessagesAsync(100).ConfigureAwait(false);
            return count.Count == expectedCount;
        }, timeout).ConfigureAwait(false);
        }
        }
    }
}
