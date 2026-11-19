using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Decides what a scheduled loop does about occurrences it missed while down or busy.
/// </summary>
/// <remarks>
///     The default is <see cref="Skip" />. Missed occurrences are logged with their count through the recurring event
///     ids whichever policy applies, so a gap is never silent.
/// </remarks>
public abstract record RecurringMissedOccurrencePolicy
{
    /// <summary>
    ///     Waits for the next occurrence and never fires for a missed one.
    /// </summary>
    public sealed record Skip : RecurringMissedOccurrencePolicy;

    /// <summary>
    ///     Fires one catch-up pass when the most recent missed occurrence is within the window.
    /// </summary>
    /// <param name="CatchupWindow">How late a missed occurrence may still fire.</param>
    /// <remarks>
    ///     At most one catch-up pass runs per gap, for the most recent missed occurrence; earlier missed occurrences
    ///     stay skipped. <see cref="RecurringCommandHostOptions.Validate" /> requires a positive window.
    /// </remarks>
    public sealed record FireOnce(TimeSpan CatchupWindow) : RecurringMissedOccurrencePolicy;
}
