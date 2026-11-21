using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Runtime.Abstractions.Scheduling;

/// <summary>
///     Decides when scheduled work runs for one tenant, as wall-clock occurrences.
/// </summary>
/// <remarks>
///     <para>
///         The per-tenant form of <see cref="ISchedule" />: the caller asks for the next occurrence for one tenant
///         after the current time before each wait, and the schedule reads calendar rules in that tenant's zone.
///     </para>
///     <para>
///         Implementations must be thread-safe and free of side effects, because callers invoke them on every wait.
///     </para>
/// </remarks>
public interface ITenantSchedule
{
    /// <summary>
    ///     Gets the next occurrence for one tenant strictly after the supplied instant.
    /// </summary>
    /// <param name="tenantId">The tenant the occurrence is computed for.</param>
    /// <param name="after">The instant the occurrence must follow.</param>
    /// <param name="cancellationToken">A token that stops the lookup.</param>
    /// <returns>The next occurrence, or <see langword="null" /> when the schedule ends.</returns>
    ValueTask<DateTimeOffset?> GetNextOccurrenceAsync(
        string tenantId,
        DateTimeOffset after,
        CancellationToken cancellationToken);
}
