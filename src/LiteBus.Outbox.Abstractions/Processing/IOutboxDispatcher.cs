using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Publishes a leased outbox message to the configured destination.
/// </summary>
/// <remarks>
///     <para>
///         Processors call a dispatcher after a store lease has been recorded. Dispatchers can publish through LiteBus
///         event handlers, a broker, an HTTP endpoint, or any other transport. The dispatcher should throw when
///         publication fails; the processor records retry or dead-letter state from that exception.
///     </para>
///     <para>
///         A dispatcher receives the serialized envelope. It is responsible for resolving the contract and deserializing
///         the payload when the target transport needs the CLR event instance.
///     </para>
/// </remarks>
public interface IOutboxDispatcher
{
    /// <summary>
    ///     Dispatches one persisted outbox message.
    /// </summary>
    /// <param name="message">The leased outbox message to dispatch.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch before the target observes the message.</param>
    /// <returns>A task that represents the asynchronous dispatch operation.</returns>
    Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Dispatches one persisted outbox message from a known delivery path.
    /// </summary>
    /// <param name="message">The leased outbox message to dispatch.</param>
    /// <param name="kind">The delivery path that started this dispatch.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch before the target observes the message.</param>
    /// <returns>A task that represents the asynchronous dispatch operation.</returns>
    /// <remarks>
    ///     The default implementation delegates to <see cref="DispatchAsync(OutboxEnvelope, CancellationToken)" />,
    ///     so transport dispatchers and test fakes keep their behavior. The in-process dispatcher overrides it to
    ///     stamp the kind into the reaction's dispatch context.
    /// </remarks>
    Task DispatchAsync(OutboxEnvelope message, OutboxDispatchKind kind, CancellationToken cancellationToken = default)
    {
        return DispatchAsync(message, cancellationToken);
    }
}
