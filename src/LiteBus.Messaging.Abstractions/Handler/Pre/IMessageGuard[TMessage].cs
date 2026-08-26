using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a message is permitted to proceed.
/// </summary>
/// <typeparam name="TMessage">The type of message this guard runs for.</typeparam>
/// <remarks>
///     <para>
///         A guard is a precondition that refuses: it answers "may this happen", and nothing else. Deciding whether the
///         answer is already known belongs to <see cref="IMessageShortcut{TMessage}" />, and the split is what lets the
///         framework run every guard before any shortcut. A cached answer can therefore never reach a caller that a
///         guard would have refused, whatever priorities are written.
///     </para>
///     <para>
///         Because the judgment is a return value, the compiler requires it. Nothing after the judgment runs by
///         accident, and an expected control-flow path stays off the exception path.
///     </para>
///     <para>
///         This shape fits every message, including one that produces a result, because a refusal does not owe the
///         caller the value the main handler would have produced. Use
///         <see cref="IMessageGuard{TMessage,TMessageResult}" /> only when the refusal should hand the caller a value
///         rather than raise <see cref="LiteBusMessageDeniedException" />.
///     </para>
/// </remarks>
public interface IMessageGuard<in TMessage> : IMessagePreHandler
    where TMessage : notnull
{
    /// <summary>
    ///     Checks whether the message is permitted to proceed.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows or refuses the message.</returns>
    Task<Verdict> CheckAsync(TMessage message, CancellationToken cancellationToken = default);
}
