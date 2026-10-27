using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Commits the work one mediation staged, such as the transaction a command handler wrote through.
/// </summary>
/// <remarks>
///     <para>
///         LiteBus calls <see cref="CommitAsync" /> once per mediation that ended with
///         <see cref="MediationOutcome.Succeeded" />, from a completion handler running at
///         <see cref="HandlerPriorities.UnitOfWork" />. That position runs after every LiteBus completion handler, so a
///         record the audit writer staged is part of the same commit, and the completion stage is the only stage that
///         sees how the mediation ended, which is why the commit lives there rather than in a post-handler.
///     </para>
///     <para>
///         It is never called for any other ending: a denial, a validation failure, a shortcut answer, a fault, or a
///         cancellation. A shortcut answer does not commit even though <see cref="MediationResult.IsSuccess" /> reports
///         it as a success, because the main handler never ran and staged nothing; committing would flush unrelated
///         ambient work, or re-apply an idempotent repeat.
///     </para>
///     <para>
///         LiteBus never opens or owns the transaction. The application opens it, usually in a pre-handler or lazily
///         from the handler through the same scoped unit of work, stages work into it, and this method commits it.
///         LiteBus simply never calls the commit when the mediation did not succeed; releasing the uncommitted work is
///         the implementation's own concern, usually in <c>Dispose</c> when the mediation scope ends.
///     </para>
///     <para>
///         A commit that throws fails the mediation with that exception. The completion stage suppresses a handler fault
///         only when the mediation had already failed; after a success the fault propagates to the caller, so a
///         concurrency conflict is reported rather than silently losing the write.
///     </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    ///     Commits the work the mediation staged.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token supplied to the completion stage.</param>
    /// <returns>A task representing the asynchronous commit operation.</returns>
    /// <remarks>
    ///     The completion stage is not cancellable, so the token is <see cref="CancellationToken.None" />; a commit
    ///     that needs a deadline applies its own.
    /// </remarks>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
