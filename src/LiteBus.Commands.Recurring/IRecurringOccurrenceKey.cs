using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Builds the stable key identifying one scheduled occurrence of a recurring loop.
/// </summary>
/// <remarks>
///     <para>
///         The replaceable port behind occurrence recording and
///         <see cref="RecurringLeaseScope.TenantOccurrence" /> leases: the loop records each scheduled occurrence it
///         runs through <see cref="IRecurringOccurrenceStore" /> under the key this port returns, and a
///         per-occurrence lease carries the key in its lease name.
///     </para>
///     <para>
///         The key must be stable across replicas for the same inputs, because two instances record and lease the
///         same occurrence independently. When <see cref="RecurringCommandHostOptions.OccurrenceKey" /> is
///         <see langword="null" />, loops use <see cref="DefaultRecurringOccurrenceKey" />.
///     </para>
/// </remarks>
public interface IRecurringOccurrenceKey
{
    /// <summary>
    ///     Creates the stable key for one scheduled occurrence.
    /// </summary>
    /// <param name="loopName">The name of the recurring loop that runs the occurrence.</param>
    /// <param name="tenantId">The tenant the occurrence belongs to, or <see langword="null" /> for a loop without tenants.</param>
    /// <param name="scheduledAt">The scheduled instant of the occurrence.</param>
    /// <returns>The stable key identifying the scheduled occurrence.</returns>
    string CreateKey(string loopName, string? tenantId, DateTimeOffset scheduledAt);
}
