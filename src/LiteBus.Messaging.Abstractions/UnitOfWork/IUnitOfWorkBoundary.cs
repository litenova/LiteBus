using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Opens and rolls back the transaction of a unit of work, for a unit of work that wants LiteBus to mark where a
///     mediation's writes begin and where a failed mediation's writes are discarded.
/// </summary>
/// <remarks>
///     <para>
///         Optional. Implement it on the type registered with <c>UseUnitOfWork</c>, beside <see cref="IUnitOfWork" />.
///         LiteBus calls <see cref="BeginAsync" /> once per mediation, after guards, validators and shortcuts have let
///         the message through and before the first pre-handler runs, so a denial, a validation failure or a shortcut
///         answer opens nothing. A command batch calls it once, before its first command.
///     </para>
///     <para>
///         When the mediation succeeds, <see cref="IUnitOfWork.CommitAsync" /> runs as usual. When it does not succeed
///         after <see cref="BeginAsync" /> ran, LiteBus calls <see cref="RollbackAsync" />. A rollback that throws is
///         recorded as a suppressed completion fault and never replaces the original failure.
///     </para>
///     <para>
///         The hook is what makes a handler that saves on its own, through its own <c>SaveChanges</c> or raw SQL,
///         part of the mediation's transaction. LiteBus still references no ORM or driver: the implementation opens
///         and rolls back the transaction.
///     </para>
/// </remarks>
public interface IUnitOfWorkBoundary
{
    /// <summary>
    ///     Opens the transaction the mediation writes through.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels opening the transaction.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task BeginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Discards the work the mediation staged after <see cref="BeginAsync" />.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The cancellation token; the completion stage passes <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
