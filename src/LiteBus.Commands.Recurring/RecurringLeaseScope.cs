namespace LiteBus.Commands.Recurring;

/// <summary>
///     Chooses what one acquired recurring lease single-flights: the whole loop, one tenant, or one scheduled
///     occurrence of one tenant.
/// </summary>
/// <remarks>
///     <para>
///         The default is <see cref="Loop" />: one lease covers the whole pass, and with
///         <see cref="RecurringCommandHostOptions.RequireLease" /> set a pass runs on at most one host instance at a
///         time. The tenant scopes apply to tenant sweeps: <see cref="Tenant" /> acquires one lease per tenant around
///         that tenant's send, so two replicas divide tenants instead of contending for the whole sweep, and
///         <see cref="TenantOccurrence" /> scopes the lease to one scheduled occurrence of one tenant.
///     </para>
///     <para>
///         The scope only shapes lease names while <see cref="RecurringCommandHostOptions.RequireLease" /> is
///         <see langword="true" />. Single-command and batch loops have no tenant per send and treat every scope as
///         <see cref="Loop" />. A sweep without <see cref="RecurringCommandHostOptions.TenantSchedule" /> has no
///         scheduled occurrence to key by and treats <see cref="TenantOccurrence" /> as <see cref="Tenant" />.
///     </para>
/// </remarks>
public enum RecurringLeaseScope
{
    /// <summary>
    ///     One lease covers the whole pass: a pass that cannot acquire it is skipped, and one acquired lease covers
    ///     the whole busy period.
    /// </summary>
    Loop = 0,

    /// <summary>
    ///     One lease covers one tenant's send: a tenant whose lease another instance holds is skipped and the sweep
    ///     continues with the next tenant.
    /// </summary>
    Tenant = 1,

    /// <summary>
    ///     One lease covers one scheduled occurrence of one tenant, keyed through
    ///     <see cref="IRecurringOccurrenceKey" />: each tenant-day runs once across replicas while different tenants
    ///     never contend.
    /// </summary>
    TenantOccurrence = 2
}
