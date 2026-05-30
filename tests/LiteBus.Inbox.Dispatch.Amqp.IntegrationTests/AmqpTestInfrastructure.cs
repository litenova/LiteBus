using System.Text;
using LiteBus.Amqp;
using RabbitMQ.Client;

namespace LiteBus.Inbox.Dispatch.Amqp.IntegrationTests;

/// <summary>
///     Helpers for declaring test topology and receiving one published message.
/// </summary>
internal static class AmqpTestInfrastructure
{
    /// <summary>
    ///     Declares a direct exchange, queue, and binding used by inbox dispatch tests.
    /// </summary>
    /// <param name="connectionUri">The broker connection URI.</param>
    /// <param name="exchange">The exchange name.</param>
    /// <param name="queue">The queue name.</param>
    /// <param name="routingKey">The routing key used for the binding.</param>
    /// <param name="cancellationToken">A token used to cancel broker operations.</param>
    /// <returns>A task that represents the asynchronous topology declaration.</returns>
    public static async Task DeclareDirectTopologyAsync(
        Uri connectionUri,
        string exchange,
        string queue,
        string routingKey,
        CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory { Uri = connectionUri };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.ExchangeDeclareAsync(
            exchange: exchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.QueueDeclareAsync(
            queue: queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.QueueBindAsync(
            queue: queue,
            exchange: exchange,
            routingKey: routingKey,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Waits for one message on the specified queue.
    /// </summary>
    /// <param name="connectionUri">The broker connection URI.</param>
    /// <param name="queue">The queue to read from.</param>
    /// <param name="timeout">The maximum time to wait for a message.</param>
    /// <param name="cancellationToken">A token used to cancel the wait.</param>
    /// <returns>The received body and headers.</returns>
    public static async Task<(string Body, IReadOnlyDictionary<string, object?> Headers)> ReceiveOneAsync(
        Uri connectionUri,
        string queue,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory { Uri = connectionUri };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await channel.BasicGetAsync(queue, autoAck: true, cancellationToken).ConfigureAwait(false);

            if (result is not null)
            {
                var body = Encoding.UTF8.GetString(result.Body.ToArray());
                var headers = NormalizeHeaders(result.BasicProperties.Headers);
                return (body, headers);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"No AMQP message arrived on queue '{queue}' within {timeout}.");
    }

    /// <summary>
    ///     Normalizes broker header values into strings for assertions.
    /// </summary>
    /// <param name="headers">The raw broker headers.</param>
    /// <returns>The normalized header dictionary.</returns>
    private static IReadOnlyDictionary<string, object?> NormalizeHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var normalized = new Dictionary<string, object?>(headers.Count, StringComparer.Ordinal);

        foreach (var (key, value) in headers)
        {
            normalized[key] = value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
                _ => value
            };
        }

        return normalized;
    }
}
