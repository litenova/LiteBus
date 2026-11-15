using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.UnitOfWork;

/// <summary>
///     Opens the unit-of-work transaction before any command pre-handler runs.
/// </summary>
/// <remarks>
///     <para>
///         Registered by <see cref="CommandModuleBuilder" /> through <c>UseUnitOfWork</c>, and only when the unit of work
///         implements <see cref="IUnitOfWorkBoundary" />. It runs at the lowest possible priority, so it opens the
///         transaction before any application pre-handler can write, and the pre-handler stage runs after guards,
///         validators and shortcuts, so a denial, a validation failure, or a shortcut answer opens nothing.
///     </para>
///     <para>
///         After <see cref="IUnitOfWorkBoundary.BeginAsync" /> returns it records a
///         <see cref="CommandUnitOfWorkBegun" /> marker in the execution-context data, which the
///         <see cref="CommandUnitOfWorkCompletionHandler{TUnitOfWork}" /> reads back to decide whether a failed
///         mediation must roll back. A <c>BeginAsync</c> that throws records no marker, so there is nothing to roll
///         back.
///     </para>
///     <para>
///         It takes the same scoped <typeparamref name="TUnitOfWork" /> instance the completion handler commits, so
///         beginning, committing and rolling back all hit one unit of work.
///     </para>
/// </remarks>
/// <typeparam name="TUnitOfWork">The application unit of work whose transaction is opened.</typeparam>
[HandlerPriority(int.MinValue)]
internal sealed class CommandUnitOfWorkBoundaryPreHandler<TUnitOfWork> : ICommandPreHandler
    where TUnitOfWork : class, IUnitOfWork
{
    /// <summary>
    ///     The unit of work whose transaction is opened before the command is handled.
    /// </summary>
    private readonly TUnitOfWork _unitOfWork;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandUnitOfWorkBoundaryPreHandler{TUnitOfWork}" /> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of work whose transaction is opened before the command is handled.</param>
    public CommandUnitOfWorkBoundaryPreHandler(TUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task PreHandleAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_unitOfWork is not IUnitOfWorkBoundary boundary)
        {
            return;
        }

        await boundary.BeginAsync(cancellationToken).ConfigureAwait(false);

        AmbientExecutionContext.GetCurrentOrDefault()?.Data.Set(new CommandUnitOfWorkBegun());
    }
}
