using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Decides whether a message is permitted to proceed.
/// </summary>
/// <typeparam name="TMessage">The type of message this guard runs for.</typeparam>
/// <remarks>
///     <para>
///         A guard is a precondition that denies: it answers "may this happen", and nothing else. Whether the answer is
///         already known belongs to <see cref="IMessageShortcut{TMessage}" />, and whether the message is well-formed
///         belongs to <see cref="IMessageValidator{TMessage}" />. The split is what lets the framework run every guard
///         before either, so a cached answer can never reach a caller that a guard would have denied, whatever
///         priorities are written.
///     </para>
///     <para>
///         The stage stops at the first denial, because one reason is enough for a caller who is not allowed to
///         proceed. The validator stage aggregates instead, because a caller fixing a malformed message wants every
///         failure at once.
///     </para>
///     <para>
///         Because the judgment is a return value, the compiler requires it. Nothing after the judgment runs by
///         accident, and an expected control-flow path stays off the exception path.
///     </para>
///     <para>
///         One contract fits every message, including one that produces a result, because a denial does not owe the
///         caller the value the main handler would have produced. Register an
///         <see cref="IMessageRefusalMapper{TMessage,TMessageResult}" /> when the application hands the caller a failed
///         result object instead of raising <see cref="LiteBusMessageDeniedException" />; the mapping then lives in one
///         place rather than in each guard.
///     </para>
/// </remarks>
public interface IMessageGuard<in TMessage> : IMessagePreStageHandler
    where TMessage : notnull
{
    /// <summary>
    ///     Decides whether the message is permitted to proceed.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows or denies the message.</returns>
    Task<Verdict> DecideAsync(TMessage message, CancellationToken cancellationToken = default);
}
