using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Queries.UnitOfWork;

/// <summary>
///     Commits the application's unit of work once a query mediation has succeeded.
/// </summary>
/// <remarks>
///     <para>
///         Registered by <see cref="QueryModuleBuilder" /> through <c>UseUnitOfWork</c>. It runs at
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
///         A commit that throws fails the mediation with that exception. The completion stage suppresses a handler fault
///         only when the mediation had already failed; after a success the fault propagates to the caller.
///     </para>
///     <para>
///         When the unit of work implements <see cref="IUnitOfWorkBoundary" />, the boundary pre-handler has already
///         called <c>BeginAsync</c> before any other pre-handler ran, and recorded a
///         <see cref="QueryUnitOfWorkBegun" /> marker in the execution-context data. On any outcome other than
///         <see cref="MediationOutcome.Succeeded" /> this handler calls <c>RollbackAsync</c> with
///         <see cref="CancellationToken.None" /> when that marker is present. A guard denial, a validation failure,
///         and a shortcut answer leave no marker, because those stages run before pre-handlers, so they roll back
///         nothing. A rollback that throws while the mediation already failed propagates to the completion stage,
///         which records it under <see cref="MediationExceptionData.SuppressedCompletionFaults" /> without replacing
///         the original failure; without an original failure it is logged instead.
///     </para>
///     <para>
///         After the commit returns, every <see cref="IUnitOfWorkCommitListener" /> resolved from the mediation scope
///         is notified in registration order. A listener that throws is logged and does not fail the mediation,
///         because the data is already committed.
///     </para>
/// </remarks>
/// <typeparam name="TUnitOfWork">The application unit of work this handler commits.</typeparam>
[HandlerPriority(HandlerPriorities.UnitOfWork)]
internal sealed class QueryUnitOfWorkCompletionHandler<TUnitOfWork> : IQueryCompletionHandler
    where TUnitOfWork : class, IUnitOfWork
{
    /// <summary>
    ///     The commit listeners notified after the unit of work commits, in registration order.
    /// </summary>
    private readonly IEnumerable<IUnitOfWorkCommitListener> _commitListeners;

    /// <summary>
    ///     Records a commit listener fault and a rollback that threw without an original failure to attach it to.
    /// </summary>
    private readonly ILogger<QueryUnitOfWorkCompletionHandler<TUnitOfWork>> _logger;

    /// <summary>
    ///     The unit of work committed at the end of mediation.
    /// </summary>
    private readonly TUnitOfWork _unitOfWork;

    /// <summary>
    ///     Initializes a new instance of the <see cref="QueryUnitOfWorkCompletionHandler{TUnitOfWork}" /> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of work committed at the end of mediation.</param>
    /// <param name="commitListeners">The commit listeners notified after the unit of work commits, or <see langword="null" /> for none.</param>
    /// <param name="logger">Records a commit listener fault and a rollback without an original failure, or <see langword="null" /> to discard it.</param>
    public QueryUnitOfWorkCompletionHandler(
        TUnitOfWork unitOfWork,
        IEnumerable<IUnitOfWorkCommitListener>? commitListeners = null,
        ILogger<QueryUnitOfWorkCompletionHandler<TUnitOfWork>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _unitOfWork = unitOfWork;
        _commitListeners = commitListeners ?? [];
        _logger = logger ?? NullLogger<QueryUnitOfWorkCompletionHandler<TUnitOfWork>>.Instance;
    }

    /// <inheritdoc />
    public async Task HandleCompletionAsync(MessageCompletionContext<IQuery> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Outcome != MediationOutcome.Succeeded)
        {
            await RollbackWhenBegunAsync(context).ConfigureAwait(false);
            return;
        }

        await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
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
    private async Task RollbackWhenBegunAsync(MessageCompletionContext<IQuery> context)
    {
        if (_unitOfWork is not IUnitOfWorkBoundary boundary)
        {
            return;
        }

        var executionContext = AmbientExecutionContext.GetCurrentOrDefault();

        if (executionContext is null || !executionContext.Data.Contains<QueryUnitOfWorkBegun>())
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
            QueryUnitOfWorkLogMessages.RollbackFailed(_logger, exception);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Notifies every registered commit listener that the unit of work committed.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token supplied to the completion stage.</param>
    /// <returns>A task representing the asynchronous notification.</returns>
    /// <remarks>
    ///     Listeners run in registration order. A listener that throws is logged and does not fail the mediation: the
    ///     data is already committed.
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
                QueryUnitOfWorkLogMessages.CommitListenerFailed(
                    _logger,
                    listener.GetType().FullName ?? listener.GetType().Name,
                    exception);
            }
#pragma warning restore CA1031
        }
    }
}
