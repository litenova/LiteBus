using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Adapts the Entity Framework context factory to the outbox operation-context contract.
/// </summary>
/// <typeparam name="TContext">The application database context type.</typeparam>
internal sealed class EfCoreOutboxDbContextFactory<TContext> : IEfCoreOutboxDbContextFactory
    where TContext : DbContext, IOutboxDbContext
{
    /// <summary>
    ///     The Entity Framework factory that owns context construction.
    /// </summary>
    private readonly IDbContextFactory<TContext> _dbContextFactory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxDbContextFactory{TContext}" /> class.
    /// </summary>
    /// <param name="dbContextFactory">The Entity Framework context factory.</param>
    public EfCoreOutboxDbContextFactory(IDbContextFactory<TContext> dbContextFactory)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        _dbContextFactory = dbContextFactory;
    }

    /// <inheritdoc />
    public async ValueTask<IOutboxDbContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        return await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
    }
}
