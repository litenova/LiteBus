using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Amqp;

/// <summary>
///     Publishes AMQP messages to a broker.
/// </summary>
public interface IAmqpPublisher
{
    /// <summary>
    ///     Publishes one message to the configured broker.
    /// </summary>
    /// <param name="request">The publication request describing exchange, routing key, body, and headers.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task that completes when the broker accepts the message.</returns>
    Task PublishAsync(AmqpPublishRequest request, CancellationToken cancellationToken = default);
}
