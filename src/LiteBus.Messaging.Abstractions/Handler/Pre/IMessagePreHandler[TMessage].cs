using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a pre-handler that runs before the main handler for messages of type <typeparamref name="TMessage" />.
/// </summary>
/// <typeparam name="TMessage">The type of message this pre-handler runs for.</typeparam>
/// <remarks>
///     <para>
///         Use this contract for validation, authorization, and enrichment. Throwing stops the pipeline and routes to
///         error handlers; the completion stage records the failure either way.
///     </para>
///     <para>
///         A pre-handler of this kind cannot stop the pipeline cleanly, which is deliberate: the ability to
///         short-circuit is a capability, so it lives in <see cref="IShortCircuitingPreHandler{TMessage}" /> instead. A
///         validator therefore cannot skip the work by accident.
///     </para>
/// </remarks>
public interface IMessagePreHandler<in TMessage> : IMessagePreHandler
    where TMessage : notnull
{
    /// <summary>
    ///     Runs before the main handler.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task representing the asynchronous pre-handling operation.</returns>
    Task PreHandleAsync(TMessage message, CancellationToken cancellationToken = default);
}
