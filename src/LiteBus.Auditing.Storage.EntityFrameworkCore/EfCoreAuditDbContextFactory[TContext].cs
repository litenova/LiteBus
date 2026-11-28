using System;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Adapts the Entity Framework context factory to the audit operation-context contract.
/// </summary>
/// <typeparam name="TContext">The application database context type.</typeparam>
/// <remarks>
///     Register this factory for the application database context through dependency injection, with an
///     Entity Framework context factory behind it, so denials, failures, cancellations, and invalid messages
///     persist outside the business transaction.
/// </remarks>
public sealed class EfCoreAuditDbContextFactory<TContext> : IAuditDbContextFactory
    where TContext : DbContext, IAuditDbContext
{
    /// <summary>
    ///     The Entity Framework factory that owns context construction.
    /// </summary>
    private readonly IDbContextFactory<TContext> _dbContextFactory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreAuditDbContextFactory{TContext}" /> class.
    /// </summary>
    /// <param name="dbContextFactory">The Entity Framework context factory.</param>
    public EfCoreAuditDbContextFactory(IDbContextFactory<TContext> dbContextFactory)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        _dbContextFactory = dbContextFactory;
    }

    /// <inheritdoc />
    public IAuditDbContext CreateDbContext()
    {
        return _dbContextFactory.CreateDbContext();
    }
}
