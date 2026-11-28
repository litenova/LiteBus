using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Takes part in the end of a command batch: work that must happen before the single commit, after it, or on
///     rollback, such as settling idempotency claims or writing buffered audit records. A feature package registers its
///     participant as a scoped collection entry of this interface.
/// </summary>
/// <remarks>
///     <para>
///         Implementations are registered as scoped services and resolved from the batch's shared scope, so a participant
///         instance lives exactly as long as one batch and may hold state the batch's commands added to it.
///     </para>
///     <para>
///         The batch calls <see cref="BeforeCommitAsync" /> on every participant before <c>IUnitOfWork.CommitAsync</c>,
///         then <see cref="AfterCommitAsync" /> after it returns. When a command stops the batch, or a step before the
///         commit or the commit itself throws, it calls <see cref="RollbackAsync" /> instead of the steps not yet run.
///     </para>
/// </remarks>
public interface ICommandBatchParticipant
{
    /// <summary>
    ///     Runs once when the batch starts, before the unit of work begins and before the first command.
    /// </summary>
    /// <param name="start">The batch identifier and the items every command of the batch receives.</param>
    /// <param name="cancellationToken">The token cancelling the batch.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    ///     A participant adds to <see cref="CommandBatchStart.CommandItems" /> what its own pipeline handlers read from
    ///     each command's execution context items, such as a buffer the commands add to and the participant drains
    ///     before the commit. The default does nothing.
    /// </remarks>
    Task BeginAsync(CommandBatchStart start, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Runs inside the transaction, after every command passed and before the single commit.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token; the batch passes <see cref="CancellationToken.None" />.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task BeforeCommitAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Runs after the single commit returned.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token; the batch passes <see cref="CancellationToken.None" />.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AfterCommitAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Runs when the batch rolls back, after the unit of work's rollback.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token; the batch passes <see cref="CancellationToken.None" />.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RollbackAsync(CancellationToken cancellationToken);
}
