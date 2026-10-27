using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

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
/// </remarks>
/// <typeparam name="TUnitOfWork">The application unit of work this handler commits.</typeparam>
[HandlerPriority(HandlerPriorities.UnitOfWork)]
internal sealed class QueryUnitOfWorkCompletionHandler<TUnitOfWork> : IQueryCompletionHandler
    where TUnitOfWork : class, IUnitOfWork
{
    /// <summary>
    ///     The unit of work committed at the end of mediation.
    /// </summary>
    private readonly TUnitOfWork _unitOfWork;

    /// <summary>
    ///     Initializes a new instance of the <see cref="QueryUnitOfWorkCompletionHandler{TUnitOfWork}" /> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of work committed at the end of mediation.</param>
    public QueryUnitOfWorkCompletionHandler(TUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<IQuery> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Outcome != MediationOutcome.Succeeded)
        {
            return Task.CompletedTask;
        }

        return _unitOfWork.CommitAsync(cancellationToken);
    }
}
