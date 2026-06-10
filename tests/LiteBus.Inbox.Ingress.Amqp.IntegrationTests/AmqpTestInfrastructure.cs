using System.Text;
using LiteBus.Transport.Amqp;
using RabbitMQ.Client;

namespace LiteBus.Inbox.Ingress.Amqp.IntegrationTests;

/// <summary>
///     Helpers for declaring test topology and receiving one published message.
/// </summary>
internal static class AmqpTestInfrastructure
{
    /// <summary>
    ///     Declares a durable queue on the default direct exchange.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <param name="queueName">The queue to declare.</param>
    /// <returns>A task that completes when the queue exists.</returns>
    public static async Task DeclareQueueAsync(AmqpConnectionOptions connectionOptions, string queueName)
    {
        await using var manager = new AmqpConnectionManager(connectionOptions);
        await using var channel = await manager.CreateChannelAsync();
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
    }

    /// <summary>
    ///     Waits for one message on the specified queue.
    /// </summary>
    /// <param name="connectionOptions">The broker connection options.</param>
    /// <param name="queue">The queue to read from.</param>
    /// <param name="timeout">The maximum time to wait for a message.</param>
    /// <returns>The received body and headers.</returns>
    public static async Task<(string Body, IReadOnlyDictionary<string, object?> Headers)> ReceiveOneAsync(
        AmqpConnectionOptions connectionOptions,
        string queue,
        TimeSpan timeout)
    {
        var uri = ResolveConnectionUri(connectionOptions);
        var factory = new ConnectionFactory { Uri = uri };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(queue, autoAck: true);
            if (result is not null)
            {
                var body = Encoding.UTF8.GetString(result.Body.ToArray());
                IReadOnlyDictionary<string, object?> headers = result.BasicProperties.Headers is null
                    ? new Dictionary<string, object?>()
                    : new Dictionary<string, object?>(result.BasicProperties.Headers);
                return (body, headers);
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No message received on queue '{queue}' within {timeout}.");
    }

    /// <summary>
    ///     Resolves a connection URI from AMQP connection options.
    /// </summary>
    /// <param name="connectionOptions">The connection options configured for the broker fixture.</param>
    /// <returns>The AMQP URI used by RabbitMQ.Client helpers.</returns>
    private static Uri ResolveConnectionUri(AmqpConnectionOptions connectionOptions)
    {
        if (connectionOptions.Uri is not null)
        {
            return connectionOptions.Uri;
        }

        return new Uri(
            $"amqp://{Uri.EscapeDataString(connectionOptions.UserName)}:{Uri.EscapeDataString(connectionOptions.Password)}@{connectionOptions.HostName}:{connectionOptions.Port}{connectionOptions.VirtualHost}");
    }
}
