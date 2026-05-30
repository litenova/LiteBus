using System;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace LiteBus.Amqp;

/// <summary>
///     Creates and reuses AMQP connections and channels for publishers and consumers.
/// </summary>
public interface IAmqpConnectionManager : IAsyncDisposable
{
    /// <summary>
    ///     Gets the shared AMQP connection, creating it on first use.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The shared broker connection.</returns>
    Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates a new AMQP channel on the shared connection.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A channel that the caller owns and must dispose.</returns>
    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);
}
