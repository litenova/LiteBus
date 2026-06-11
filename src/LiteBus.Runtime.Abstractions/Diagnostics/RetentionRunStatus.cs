using System;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     Describes the most recent retention cleanup pass and configured policy.
/// </summary>
/// <param name="Enabled">Whether the retention background loop is enabled.</param>
/// <param name="Retention">The configured retention period, when set.</param>
/// <param name="Interval">The delay between automatic cleanup passes.</param>
/// <param name="LastRunAt">The UTC timestamp of the last cleanup attempt, when one has run.</param>
/// <param name="LastDeletedCount">The number of rows deleted during the last successful pass.</param>
/// <param name="LastError">The message from the last failed pass, when applicable.</param>
public sealed record RetentionRunStatus(
    bool Enabled,
    TimeSpan? Retention,
    TimeSpan Interval,
    DateTimeOffset? LastRunAt,
    int LastDeletedCount,
    string? LastError);