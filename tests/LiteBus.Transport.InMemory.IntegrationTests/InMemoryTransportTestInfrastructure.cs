using System.Text;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.InMemory.IntegrationTests;

/// <summary>
///     Shared helpers for in-memory transport integration tests.
/// </summary>
internal static class InMemoryTransportTestInfrastructure
{
    /// <summary>
    ///     Creates a unique destination name for the current test run.
    /// </summary>
    /// <param name="prefix">The prefix that identifies the scenario under test.</param>
    /// <returns>A destination name safe for in-memory transport routing.</returns>
    public static string CreateDestination(string prefix)
    {
        return $"litebus-inmemory-{prefix}-{Guid.NewGuid():N}";
    }

    /// <summary>
    ///     Starts a consumer that completes the supplied task source when one message arrives.
    /// </summary>
    /// <param name="broker">The shared in-memory broker backing the consumer.</param>
    /// <param name="destination">The destination name to subscribe to.</param>
    /// <param name="received">The task source completed with the first received message.</param>
    /// <returns>The started consumer that the caller must stop and dispose.</returns>
    public static async Task<InMemoryConsumer> StartReceiveOneAsync(
        InMemoryTransportBroker broker,
        string destination,
        TaskCompletionSource<TransportMessage> received)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(received);

        var consumer = new InMemoryConsumer(broker);
        await consumer.StartAsync(
            new TransportConsumerOptions { Destination = destination },
            async (message, cancellationToken) =>
            {
                received.TrySetResult(message);
                await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return consumer;
    }

    /// <summary>
    ///     Waits until the supplied condition becomes true or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition polled until it returns <see langword="true" />.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the condition becomes true.</returns>
    public static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Reads the transport message body as UTF-8 text.
    /// </summary>
    /// <param name="message">The received transport message.</param>
    /// <returns>The decoded message body.</returns>
    public static string ReadBody(TransportMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Encoding.UTF8.GetString(message.Body.Span);
    }

    /// <summary>
    ///     Gets a required string header value from a transport message.
    /// </summary>
    /// <param name="message">The received transport message.</param>
    /// <param name="headerName">The header name to read.</param>
    /// <returns>The header value as a string.</returns>
    public static string GetHeader(TransportMessage message, string headerName)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Headers.TryGetValue(headerName, out var value);
        return value?.ToString() ?? string.Empty;
    }
}
