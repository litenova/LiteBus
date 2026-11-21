using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     What a scheduled loop does at one instant given the last occurrence it ran.
/// </summary>
/// <param name="CatchUp">The missed occurrence to run one catch-up pass for now, or <see langword="null" /> for none.</param>
/// <param name="Next">The next occurrence to wait for, or <see langword="null" /> when the schedule ended.</param>
/// <param name="MissedCount">How many occurrences passed since the last recorded one without running.</param>
/// <param name="Truncated">
///     <see langword="true" /> when more occurrences were missed than were counted; the loop then skips to the next
///     occurrence after the current time without a catch-up pass.
/// </param>
internal readonly record struct RecurringOccurrencePlan(
    DateTimeOffset? CatchUp,
    DateTimeOffset? Next,
    int MissedCount,
    bool Truncated);
