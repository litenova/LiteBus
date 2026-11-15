using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Batching;
using LiteBus.Commands.Idempotency;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Commands.UnitOfWork;

/// <summary>
///     Commits the application's unit of work once a command mediation has succeeded.
/// </summary>
/// <remarks>
///     <para>
///         Registered by <see cref="CommandModuleBuilder" /> through <c>UseUnitOfWork</c>. It runs at
///         <see cref="HandlerPriorities.UnitOfWork" />, after every LiteBus completion handler, so a record the audit
///         writer staged is part of the same commit.
///     </para>
///     <para>
///         It commits only when <see cref="MessageCompletionContext.Outcome" /> is
///         <see cref="MediationOutcome.Succeeded" />. A denial, a validation failure, a shortcut answer, a fault, and a
///         cancellation all end here without committing: the main handler either never ran or did not finish, so there
///         is nothing to commit.
///     </para>
///     <para>
///         After the commit returns it settles the idempotency claim the mediation deferred, when there is one: a store
///         that commits its own writes (see <see cref="ISeparatelyCommittedIdempotencyStore" />) cannot be completed
///         before the commit, so the idempotency completion handler records a
///         <see cref="PendingIdempotencySettlement" /> instead of settling, and this handler completes the key here. A
///         completion that throws is retried three times and then logged without failing the mediation, because the
///         data is already committed. When the commit itself throws, the deferred claim is released and the commit
///         exception is rethrown unchanged.
///     </para>
///     <para>
///         A commit that throws fails the mediation with that exception. The completion stage suppresses a handler fault
///         only when the mediation had already failed; after a success the fault propagates to the caller.
///     </para>
///     <para>
///         When the unit of work implements <see cref="IUnitOfWorkBoundary" />, the boundary pre-handler has already
///         called <c>BeginAsync</c> before any other pre-handler ran, and recorded a
///         <see cref="CommandUnitOfWorkBegun" /> marker in the execution-context data. On any outcome other than
///         <see cref="MediationOutcome.Succeeded" /> this handler calls <c>RollbackAsync</c> with
///         <see cref="CancellationToken.None" /> when that marker is present. A guard denial, a validation failure,
///         and a shortcut answer leave no marker, because those stages run before pre-handlers, so they roll back
///         nothing. A rollback that throws while the mediation already failed propagates to the completion stage,
///         which records it under <see cref="MediationExceptionData.SuppressedCompletionFaults" /> without replacing
///         the original failure; without an original failure it is logged instead.
///     </para>
///     <para>
///         After the commit returns and the deferred idempotency settlement is complete, every
///         <see cref="IUnitOfWorkCommitListener" /> resolved from the mediation scope is notified in registration
///         order. A listener that throws is logged and does not fail the mediation, because the data is already
///         committed.
///     </para>
///     <para>
///         Inside a command batch this handler does nothing: it neither commits nor rolls back, and it notifies no
///         listener. The batch calls <c>BeginAsync</c> once before its first command, <c>CommitAsync</c> once after
///         all commands passed, <c>RollbackAsync</c> when the batch stops or the commit throws, and every commit
///         listener once after its single commit.
///     </para>
/// </remarks>
/// <typeparam name="TUnitOfWork">The application unit of work this handler commits.</typeparam>
[HandlerPriority(HandlerPriorities.UnitOfWork)]
internal sealed class CommandUnitOfWorkCompletionHandler<TUnitOfWork> : ICommandCompletionHandler
    where TUnitOfWork : class, IUnitOfWork
{
    /// <summary>
    ///     The delays before the first, second and third retry of a post-commit idempotency completion.
    /// </summary>
    private static readonly TimeSpan[] PostCommitRetryDelays =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200)
    ];

    /// <summary>
    ///     Tells whether the mediation scope belongs to a command batch, or <see langword="null" /> when the handler
    ///     was built without a batch context.
    /// </summary>
    private readonly CommandBatchContext? _batchContext;

    /// <summary>
    ///     The commit listeners notified after the unit of work commits, in registration order.
    /// </summary>
    private readonly IEnumerable<IUnitOfWorkCommitListener> _commitListeners;

    /// <summary>
    ///     Records post-commit work that failed without failing the mediation, such as an idempotency settlement that
    ///     kept failing after its retries.
    /// </summary>
    private readonly ILogger<CommandUnitOfWorkCompletionHandler<TUnitOfWork>> _logger;

    /// <summary>
    ///     Remembers which keys have been applied, or <see langword="null" /> when idempotency is not enabled.
    /// </summary>
    private readonly IIdempotencyStore? _store;

    /// <summary>
    ///     Supplies the delay between post-commit idempotency completion retries.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The unit of work committed at the end of mediation.
    /// </summary>
    private readonly TUnitOfWork _unitOfWork;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandUnitOfWorkCompletionHandler{TUnitOfWork}" /> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of work committed at the end of mediation.</param>
    /// <param name="store">Remembers which keys have been applied, or <see langword="null" /> when idempotency is not enabled.</param>
    /// <param name="timeProvider">Supplies the delay between post-commit idempotency completion retries, or <see langword="null" /> for <see cref="TimeProvider.System" />.</param>
    /// <param name="logger">Records post-commit work that failed without failing the mediation, or <see langword="null" /> to discard it.</param>
    /// <param name="commitListeners">The commit listeners notified after the unit of work commits, or <see langword="null" /> for none.</param>
    /// <param name="batchContext">Tells whether the mediation scope belongs to a command batch, or <see langword="null" /> for a single mediation.</param>
    public CommandUnitOfWorkCompletionHandler(
        TUnitOfWork unitOfWork,
        IIdempotencyStore? store = null,
        TimeProvider? timeProvider = null,
        ILogger<CommandUnitOfWorkCompletionHandler<TUnitOfWork>>? logger = null,
        IEnumerable<IUnitOfWorkCommitListener>? commitListeners = null,
        CommandBatchContext? batchContext = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _unitOfWork = unitOfWork;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CommandUnitOfWorkCompletionHandler<TUnitOfWork>>.Instance;
        _commitListeners = commitListeners ?? [];
        _batchContext = batchContext;
    }

    /// <inheritdoc />
    public async Task HandleCompletionAsync(MessageCompletionContext<ICommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_batchContext?.IsActive == true)
        {
            return;
        }

        if (context.Outcome != MediationOutcome.Succeeded)
        {
            await RollbackWhenBegunAsync(context).ConfigureAwait(false);
            return;
        }

#pragma warning disable CA1031 // The deferred claim must be released whatever the commit threw, and the caller must still get the commit exception.
        try
        {
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await ReleasePendingSettlementAsync().ConfigureAwait(false);
            throw;
        }
#pragma warning restore CA1031

        await CompletePendingSettlementAsync().ConfigureAwait(false);
        await NotifyCommitListenersAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Rolls back the unit-of-work transaction when the mediation opened one and did not succeed.
    /// </summary>
    /// <param name="context">The completion context describing how the mediation ended.</param>
    /// <returns>A task representing the asynchronous rollback.</returns>
    /// <remarks>
    ///     A rollback that throws while the mediation already failed is left to propagate: the completion stage
    ///     records it under <see cref="MediationExceptionData.SuppressedCompletionFaults" /> without replacing the
    ///     original failure. Without an original failure it is logged instead, so a rollback fault still cannot fail a
    ///     mediation that ended without one.
    /// </remarks>
    private async Task RollbackWhenBegunAsync(MessageCompletionContext<ICommand> context)
    {
        if (_unitOfWork is not IUnitOfWorkBoundary boundary)
        {
            return;
        }

        var executionContext = AmbientExecutionContext.GetCurrentOrDefault();

        if (executionContext is null || !executionContext.Data.Contains<CommandUnitOfWorkBegun>())
        {
            return;
        }

#pragma warning disable CA1031 // A rollback fault must be recorded without replacing the failure that ended the mediation.
        try
        {
            await boundary.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (context.Exception is null)
        {
            CommandUnitOfWorkLogMessages.RollbackFailed(_logger, exception);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Notifies every registered commit listener that the unit of work committed.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token supplied to the completion stage.</param>
    /// <returns>A task representing the asynchronous notification.</returns>
    /// <remarks>
    ///     Listeners run in registration order, after the deferred idempotency settlement is complete, so a crash
    ///     between the two leaves the key settled and the staged envelopes to the background processor rather than
    ///     leaving the key claimed for work a repeat would run again. A listener that throws is logged and does not
    ///     fail the mediation: the data is already committed.
    /// </remarks>
    private async Task NotifyCommitListenersAsync(CancellationToken cancellationToken)
    {
        foreach (var listener in _commitListeners)
        {
#pragma warning disable CA1031 // A listener runs after the data is committed, so its fault is logged and never fails the mediation.
            try
            {
                await listener.OnCommittedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                CommandUnitOfWorkLogMessages.CommitListenerFailed(
                    _logger,
                    listener.GetType().FullName ?? listener.GetType().Name,
                    exception);
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    ///     Completes the deferred idempotency claim, retrying a failure three times before logging it.
    /// </summary>
    /// <returns>A task representing the asynchronous settlement.</returns>
    /// <remarks>
    ///     A completion that still fails after its retries is logged and does not fail the mediation: the data is
    ///     already committed. The key stays claimed until the store's claim timeout, after which a repeat runs again.
    /// </remarks>
    private async Task CompletePendingSettlementAsync()
    {
        var store = _store;

        if (store is null || !TryGetPendingSettlement(out var pending))
        {
            return;
        }

        for (var attempt = 0; ; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(PostCommitRetryDelays[attempt - 1], _timeProvider, CancellationToken.None).ConfigureAwait(false);
            }

#pragma warning disable CA1031 // Any failure leaves the key claimed, so every kind is retried the same way.
            try
            {
                await store.CompleteAsync(pending.Claimed.Value, pending.Claimed.ClaimToken, pending.Payload, CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (Exception exception)
            {
                if (attempt >= PostCommitRetryDelays.Length)
                {
                    IdempotencySettlementLogMessages.PostCommitCompleteFailed(_logger, pending.Claimed.Value, exception);
                    return;
                }
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    ///     Releases the deferred idempotency claim after the commit threw, preserving the commit exception.
    /// </summary>
    /// <returns>A task representing the asynchronous release.</returns>
    /// <remarks>
    ///     A release that throws is logged and never replaces the commit exception the caller must see. The key stays
    ///     claimed until the store's claim timeout, after which a repeat runs again.
    /// </remarks>
    private async Task ReleasePendingSettlementAsync()
    {
        var store = _store;

        if (store is null || !TryGetPendingSettlement(out var pending))
        {
            return;
        }

#pragma warning disable CA1031 // A release failure must not replace the commit exception, so it is logged and dropped.
        try
        {
            await store.ReleaseAsync(pending.Claimed.Value, pending.Claimed.ClaimToken, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            IdempotencySettlementLogMessages.PostCommitReleaseFailed(_logger, pending.Claimed.Value, exception);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Reads the deferred idempotency settlement recorded for this mediation, when there is one.
    /// </summary>
    /// <param name="pending">When this method returns <see langword="true" />, the deferred settlement.</param>
    /// <returns>
    ///     <see langword="true" /> when the mediation deferred a settlement; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    private static bool TryGetPendingSettlement([NotNullWhen(true)] out PendingIdempotencySettlement? pending)
    {
        pending = null;

        var executionContext = AmbientExecutionContext.GetCurrentOrDefault();

        if (executionContext is null || !executionContext.Data.TryGet<PendingIdempotencySettlement>(out var recorded))
        {
            return false;
        }

        pending = recorded;
        return true;
    }
}
