using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Sends several commands as one atomic batch: they run one at a time in one dispatch scope, and the unit of work
///     commits once after all of them, or not at all.
/// </summary>
/// <remarks>
///     <para>
///         Every command runs its full pipeline: guards, validators, shortcuts, pre-handlers, the handler, post-handlers
///         and completion handlers. The batch stops at the first command that is denied, invalid, faulted or cancelled;
///         later commands do not run, nothing is committed, and the unit of work is rolled back when it implements
///         <c>IUnitOfWorkBoundary</c>. A shortcut answer, such as an idempotent repeat, does not stop the batch.
///     </para>
///     <para>
///         All commands resolve their services from one scope, so they share the scoped unit of work, the
///         transactional outbox and the domain event collector. Commands never run in parallel. A batch cannot start
///         inside another batch. Without a registered unit of work the batch still stops at the first failure, but
///         atomicity then depends entirely on the application.
///     </para>
/// </remarks>
public interface ICommandBatchMediator
{
    /// <summary>
    ///     Sends the commands as one batch and throws when it does not commit.
    /// </summary>
    /// <param name="commands">The commands, run in list order.</param>
    /// <param name="commandBatchSettings">The settings applied to every command, or <see langword="null" />.</param>
    /// <param name="cancellationToken">The token checked before each command.</param>
    /// <returns>A task that completes when the batch has committed.</returns>
    /// <remarks>
    ///     A refusal rethrows the exception <c>SendAsync</c> would raise for the failing command; a fault or a failed
    ///     commit rethrows that exception. The rollback steps run before the exception propagates.
    /// </remarks>
    Task SendManyAsync(
        IReadOnlyList<ICommand> commands,
        CommandBatchSettings? commandBatchSettings = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Sends the commands as one batch and reports a refusal as a result instead of throwing.
    /// </summary>
    /// <param name="commands">The commands, run in list order.</param>
    /// <param name="commandBatchSettings">The settings applied to every command, or <see langword="null" />.</param>
    /// <param name="cancellationToken">The token checked before each command.</param>
    /// <returns>The outcome of the batch and of each command.</returns>
    /// <remarks>
    ///     A denial or validation failure returns <see cref="CommandBatchOutcome.RolledBack" /> with
    ///     <see cref="CommandBatchResult.FailedIndex" /> set. A fault, a cancellation or a failed commit propagates as an
    ///     exception after the rollback steps have run.
    /// </remarks>
    Task<CommandBatchResult> TrySendManyAsync(
        IReadOnlyList<ICommand> commands,
        CommandBatchSettings? commandBatchSettings = null,
        CancellationToken cancellationToken = default);
}
