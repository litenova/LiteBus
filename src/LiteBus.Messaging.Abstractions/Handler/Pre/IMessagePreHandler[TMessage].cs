using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Prepares a message that is going to be handled.
/// </summary>
/// <typeparam name="TMessage">The type of message this pre-handler runs for.</typeparam>
/// <remarks>
///     <para>
///         This contract is named for its position rather than its role, and deliberately so: it is the one pre-stage
///         contract whose role LiteBus does not name. Enrichment, tracing, opening a unit of work, and anything else a
///         message needs before it is handled all live here.
///     </para>
///     <para>
///         The three roles LiteBus does name have their own contracts, because the framework acts on the difference.
///         <see cref="IMessageGuard{TMessage}" /> refuses, <see cref="IMessageValidator{TMessage}" /> reports the
///         message malformed, and <see cref="IMessageShortcut{TMessage}" /> answers work that is already done. Reach for
///         one of those when it fits; reach for this contract when none does.
///     </para>
///     <para>
///         A pre-handler cannot stop the pipeline by returning, which is deliberate: deciding whether the work happens
///         is a capability that belongs to the three decision stages, so a pre-handler cannot skip the work by accident.
///         Throwing stops the pipeline and routes to error handlers, and the completion stage records the failure either
///         way.
///     </para>
///     <para>
///         This stage runs after all three decision stages, so a pre-handler only ever sees a message that every guard
///         allowed, every validator accepted, and no shortcut answered.
///     </para>
/// </remarks>
public interface IMessagePreHandler<in TMessage> : IMessagePreStageHandler
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
