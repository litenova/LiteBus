using System;
using LiteBus.Auditing.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Opens a fresh audit database context for each store operation through the supplied context factory.
/// </summary>
internal sealed class EfCoreAuditTestContextFactory : IAuditDbContextFactory
{
    /// <summary>
    ///     The delegate that creates each audit database context.
    /// </summary>
    private readonly Func<IAuditDbContext> _createContext;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreAuditTestContextFactory" /> class.
    /// </summary>
    /// <param name="createContext">The delegate that creates each audit database context.</param>
    public EfCoreAuditTestContextFactory(Func<IAuditDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);

        _createContext = createContext;
    }

    /// <summary>
    ///     Creates a new audit database context from the supplied delegate.
    /// </summary>
    /// <returns>The created context. The caller disposes it.</returns>
    public IAuditDbContext CreateDbContext()
    {
        return _createContext();
    }
}
