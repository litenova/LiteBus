using System;
using System.Data;
using LiteBus.Messaging.Abstractions;
using Marten;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Opens Marten sessions for the tenant the current mediation runs in.
/// </summary>
/// <remarks>
///     <para>
///         The factory reads the ambient execution identity and opens a lightweight session for its tenant, so
///         handlers work on tenant-filtered documents without resolving the tenant themselves. Without an ambient
///         mediation, or when the mediation runs outside any tenant, it opens a default session instead.
///     </para>
///     <para>
///         The application must configure Marten for conjoined tenancy, with
///         <c>options.Policies.AllDocumentsAreMultiTenanted()</c> and no separate-database tenancy source. A source
///         such as <c>options.MultiTenantedWithSingleServer(...)</c> keeps each tenant in its own database and
///         disables the default tenant, so the default session this factory opens outside any tenant would fail. On
///         a store without tenancy, opening a tenant session fails in Marten rather than filtering silently.
///     </para>
///     <para>
///         The factory holds no mutable state and opens a new session per call, so one singleton instance serves
///         every mediation. The caller owns the returned session and disposes it when done. Registered through
///         <see cref="CommandModuleBuilderMartenExtensions.UseMartenTenantSessions(CommandModuleBuilder)" />.
///     </para>
/// </remarks>
public sealed class MartenTenantSessionFactory
{
    /// <summary>
    ///     Gets the document store each call opens its session from.
    /// </summary>
    private readonly IDocumentStore _documentStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenTenantSessionFactory" /> class.
    /// </summary>
    /// <param name="documentStore">The application-owned Marten document store.</param>
    public MartenTenantSessionFactory(IDocumentStore documentStore)
    {
        ArgumentNullException.ThrowIfNull(documentStore);

        _documentStore = documentStore;
    }

    /// <summary>
    ///     Resolves the tenant the current mediation runs in.
    /// </summary>
    /// <returns>
    ///     The tenant identifier of the ambient execution identity, or <see langword="null" /> when no mediation is
    ///     ambient or the mediation runs outside any tenant.
    /// </returns>
    public static string? ResolveTenantId()
    {
        var identity = AmbientExecutionContext.GetCurrentOrDefault()?.Identity;

        return identity is { HasTenant: true } ? identity.TenantId : null;
    }

    /// <summary>
    ///     Opens a lightweight session for the ambient tenant, or a default session outside any tenant.
    /// </summary>
    /// <returns>The session the caller works through and disposes.</returns>
    /// <remarks>
    ///     The session is lightweight: no identity map and no automatic dirty checking, the same session the
    ///     idempotency store claims through.
    /// </remarks>
    public IDocumentSession OpenSession()
    {
        var tenantId = ResolveTenantId();

        return tenantId is null
            ? _documentStore.LightweightSession(IsolationLevel.ReadCommitted)
            : _documentStore.LightweightSession(tenantId, IsolationLevel.ReadCommitted);
    }

    /// <summary>
    ///     Opens a read-only query session for the ambient tenant, or a default session outside any tenant.
    /// </summary>
    /// <returns>The query session the caller reads through and disposes.</returns>
    public IQuerySession OpenQuerySession()
    {
        var tenantId = ResolveTenantId();

        return tenantId is null
            ? _documentStore.QuerySession()
            : _documentStore.QuerySession(tenantId);
    }
}
