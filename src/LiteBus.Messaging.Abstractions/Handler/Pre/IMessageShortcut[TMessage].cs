using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a pre-handler that answers a message whose work has already been applied, so the main handler never
///     runs.
/// </summary>
/// <typeparam name="TMessage">The type of message this shortcut runs for.</typeparam>
/// <remarks>
///     <para>
///         A shortcut answers "is this already done", and nothing else. Denying belongs to
///         <see cref="IMessageGuard{TMessage}" /> and well-formedness to <see cref="IMessageValidator{TMessage}" />, and
///         the split is what lets the framework run both of those first. The mediation reports
///         <see cref="MediationOutcome.Answered" /> and an audit trail records a success, because skipping redundant work
///         denied nobody.
///     </para>
///     <para>
///         Shortcuts run after guards and validators and before pre-handlers. Running last among the decision stages
///         means a shortcut only ever sees a message the caller was allowed to send and whose contents are well-formed,
///         so a malformed message cannot claim an idempotency key. Running before pre-handlers means a message that is
///         about to be skipped does not pay for the enrichment it would have skipped anyway; a shortcut that needs
///         prepared state resolves it from the container rather than relying on a pre-handler.
///     </para>
///     <para>
///         This shape is for messages that produce no result. Use
///         <see cref="IMessageShortcut{TMessage,TMessageResult}" /> for a message that produces one, so the compiler
///         checks the value the shortcut supplies.
///     </para>
///     <para>
///         Answering means skipping the work. Once the main handler has run there is nothing left to skip; a handler
///         that wants to suppress the reactions to a no-op calls
///         <see cref="IExecutionContext.SuppressPostHandlers" /> instead.
///     </para>
/// </remarks>
public interface IMessageShortcut<in TMessage> : IMessagePreStageHandler
    where TMessage : notnull
{
    /// <summary>
    ///     Answers the message when its work has already been applied.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>
    ///     <see cref="Shortcut.Answer" /> when the main handler should be skipped, or <see cref="Shortcut.None" /> to let
    ///     the mediation proceed.
    /// </returns>
    Task<Shortcut> TryAnswerAsync(TMessage message, CancellationToken cancellationToken = default);
}
