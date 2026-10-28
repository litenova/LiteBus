using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using Marten;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Commits the scoped Marten document session as the mediation unit of work.
/// </summary>
/// <remarks>
///     <para>
///         Registered through <c>UseMartenUnitOfWork</c>, which delegates to the command <c>UseUnitOfWork</c> hook: the
///         instance is resolved scoped as <see cref="IUnitOfWork" />, and the completion handler the hook registers
///         calls <see cref="CommitAsync" /> only when the mediation completed successfully, at
///         <see cref="HandlerPriorities.UnitOfWork" />. A denied, invalid, failed, or cancelled mediation disposes the
///         session without saving, so staged documents are rolled back by construction.
///     </para>
///     <para>
///         The application registers Marten itself with <c>AddMarten</c>, which supplies the scoped
///         <see cref="IDocumentSession" /> this instance commits. Query mediations reuse the same instance through the
///         query <c>UseUnitOfWork</c> hook; this package ships no query builder extension so that a command storage
///         package does not pull the query axis onto its consumers.
///     </para>
/// </remarks>
public sealed class MartenUnitOfWork : IUnitOfWork
{
    /// <summary>
    ///     Gets the scoped document session committed by <see cref="CommitAsync" />.
    /// </summary>
    private readonly IDocumentSession _session;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenUnitOfWork" /> class.
    /// </summary>
    /// <param name="session">The scoped document session the handler staged its documents on.</param>
    public MartenUnitOfWork(IDocumentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        return _session.SaveChangesAsync(cancellationToken);
    }
}
