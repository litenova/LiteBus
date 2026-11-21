using System.Collections.Generic;
using System.Threading;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Lists the tenants one pass of a recurring per-tenant sweep sends for.
/// </summary>
/// <remarks>
///     <para>
///         The replaceable port behind
///         <see cref="CommandModuleBuilderRecurringExtensions.AddRecurringForEachTenant{TCommand}(CommandModuleBuilder, System.Func{System.String, TCommand}, System.Action{RecurringCommandHostOptions})" />:
///         each pass enumerates the tenants, builds one command per tenant, and sends it with
///         <see cref="LiteBus.Messaging.Abstractions.ExecutionIdentity.ForTenant" />. An application whose tenants live
///         in a database implements this interface over its store.
///     </para>
///     <para>
///         Resolved from a fresh dispatch scope on every pass when registered by type or through dependency injection,
///         so an implementation can take scoped dependencies such as a database context.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class StoreTenantSource : IRecurringTenantSource
/// {
///     private readonly TenantStore _store;
///
///     public StoreTenantSource(TenantStore store)
///     {
///         _store = store;
///     }
///
///     public async IAsyncEnumerable<string> GetTenantsAsync(CancellationToken cancellationToken)
///     {
///         await foreach (var tenant in _store.ListIdsAsync(cancellationToken).ConfigureAwait(false))
///         {
///             yield return tenant;
///         }
///     }
/// }
/// ]]></code>
/// </example>
public interface IRecurringTenantSource
{
    /// <summary>
    ///     Lists the tenant identifiers one pass sends for.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the read when the host shuts down or the lease is lost.</param>
    /// <returns>The tenant identifiers, in the order the pass sends for them.</returns>
    IAsyncEnumerable<string> GetTenantsAsync(CancellationToken cancellationToken);
}
