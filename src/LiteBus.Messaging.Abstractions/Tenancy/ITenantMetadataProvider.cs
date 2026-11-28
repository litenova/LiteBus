using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Reads tenant attributes for schedules, policies and settings.
/// </summary>
/// <remarks>
///     <para>
///         This is the port tenant-aware features read through. Schedules resolve per-tenant zones from
///         <see cref="TenantMetadata.TimeZoneId" />, checkpoint calendars and policies read the culture and the
///         attributes, and none of them take a tenant store dependency to do it. The application implements this
///         interface over whatever holds its tenant records.
///     </para>
///     <para>
///         LiteBus ships no tenant store and no settings store. Dated per-tenant settings stay in the application,
///         which reaches them through its own services with the tenant from the mediation context.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class WorkspaceMetadataProvider : ITenantMetadataProvider
/// {
///     private readonly IWorkspaceStore _store;
///
///     public WorkspaceMetadataProvider(IWorkspaceStore store)
///     {
///         _store = store;
///     }
///
///     public async ValueTask<IReadOnlyDictionary<string, TenantMetadata>> GetManyAsync(
///         IReadOnlyList<string> tenantIds,
///         CancellationToken cancellationToken)
///     {
///         var workspaces = await _store.LoadAsync(tenantIds, cancellationToken).ConfigureAwait(false);
///         var result = new Dictionary<string, TenantMetadata>(StringComparer.Ordinal);
///
///         foreach (var workspace in workspaces)
///         {
///             result[workspace.Id] = new TenantMetadata(
///                 workspace.Id,
///                 workspace.TimeZoneId,
///                 workspace.CultureName,
///                 workspace.Attributes);
///         }
///
///         return result;
///     }
/// }
/// ]]></code>
/// </example>
public interface ITenantMetadataProvider
{
    /// <summary>
    ///     Reads attributes for the named tenants in one call.
    /// </summary>
    /// <param name="tenantIds">The tenant identifiers.</param>
    /// <param name="cancellationToken">A token that stops the read.</param>
    /// <returns>The attributes found, keyed by tenant identifier.</returns>
    /// <remarks>
    ///     Tenants with no record are absent from the result rather than mapping to
    ///     <see langword="null" />. Callers that need one tenant pass a single-element list.
    /// </remarks>
    ValueTask<IReadOnlyDictionary<string, TenantMetadata>> GetManyAsync(
        IReadOnlyList<string> tenantIds,
        CancellationToken cancellationToken);
}
