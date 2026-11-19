using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     One scheduled occurrence a recurring loop has recorded as run.
/// </summary>
/// <param name="LoopName">The name of the recurring loop that ran the occurrence.</param>
/// <param name="TenantId">The tenant the occurrence belongs to, or <see langword="null" /> for a loop without tenants.</param>
/// <param name="OccurrenceKey">The stable key identifying the scheduled occurrence.</param>
/// <param name="ScheduledAt">The scheduled instant of the occurrence.</param>
/// <param name="RecordedAt">The instant the occurrence was recorded.</param>
public sealed record RecurringOccurrence(string LoopName, string? TenantId, string OccurrenceKey, DateTimeOffset ScheduledAt, DateTimeOffset RecordedAt);
