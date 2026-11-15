using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Runs work right after a unit of work has committed, such as dispatching the outbox envelopes the mediation
///     staged.
/// </summary>
/// <remarks>
///     <para>
///         Register implementations as scoped services. The command and query unit-of-work completion handlers resolve
///         every registered listener from the mediation scope and call them in registration order after
///         <see cref="IUnitOfWork.CommitAsync" /> returns. A command batch calls them once, after its single commit, and
///         never per command.
///     </para>
///     <para>
///         A listener runs after the data is committed, so a listener that throws is logged and does not fail the
///         mediation.
///     </para>
/// </remarks>
public interface IUnitOfWorkCommitListener
{
    /// <summary>
    ///     Runs after the unit of work committed.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token supplied by the committing step.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnCommittedAsync(CancellationToken cancellationToken = default);
}
