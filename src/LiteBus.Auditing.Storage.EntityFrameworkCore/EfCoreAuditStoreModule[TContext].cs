using System;
using LiteBus.Runtime.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Registers the database context ports the Entity Framework Core audit store and trail resolve.
/// </summary>
/// <typeparam name="TContext">The application database context that maps the audit tables.</typeparam>
/// <remarks>
///     <para>
///         Two default ports are registered, and an earlier explicit registration of either one wins:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 <see cref="IAuditDbContext" />, scoped, resolved as the scope's <typeparamref name="TContext" />.
///                 The audit trail stages a successful mediation's record on it, so the record commits with the
///                 handler change.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <see cref="IAuditDbContextFactory" />, a singleton <see cref="EfCoreAuditDbContextFactory{TContext}" />
///                 over <c>IDbContextFactory&lt;TContext&gt;</c>. The singleton chain store opens a short context from
///                 it for every operation, and the trail writes out-of-band records on one. The application
///                 registers the context with <c>AddDbContextFactory&lt;TContext&gt;</c>, which supplies both the
///                 factory and the scoped context.
///             </description>
///         </item>
///     </list>
/// </remarks>
public sealed class EfCoreAuditStoreModule<TContext> : IModule
    where TContext : DbContext, IAuditDbContext
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(DependencyDescriptor.ForDefaultPort(
            typeof(IAuditDbContext),
            provider => provider.GetService(typeof(TContext))
                ?? throw new InvalidOperationException(
                    $"The Entity Framework Core audit store needs the application database context '{typeof(TContext).FullName}'. Register it with AddDbContextFactory<{typeof(TContext).Name}>()."),
            InstanceLifetime.Scoped));

        configuration.DependencyRegistry.Register(DependencyDescriptor.ForDefaultPort(
            typeof(IAuditDbContextFactory),
            typeof(EfCoreAuditDbContextFactory<TContext>),
            InstanceLifetime.Singleton));
    }
}
