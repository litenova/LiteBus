using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Plans what a scheduled loop does about the occurrences between its last recorded one and now.
/// </summary>
/// <remarks>
///     <para>
///         Shared by the loop behind <see cref="RecurringCommandHostOptions.Schedule" /> and by tenant sweeps
///         behind <see cref="RecurringCommandHostOptions.TenantSchedule" />, so both evaluate the same missed
///         occurrences the same way. The caller logs <see cref="RecurringOccurrencePlan.MissedCount" />, runs one
///         pass at once when <see cref="RecurringOccurrencePlan.CatchUp" /> is set, and otherwise waits for
///         <see cref="RecurringOccurrencePlan.Next" />.
///     </para>
/// </remarks>
internal static class RecurringOccurrencePlanner
{
    /// <summary>
    ///     The most missed occurrences one evaluation counts before skipping to the next occurrence after now.
    /// </summary>
    private const int MaxCountedMissedOccurrences = 1000;

    /// <summary>
    ///     Plans what a scheduled loop does at <paramref name="basis" /> given the last occurrence it ran.
    /// </summary>
    /// <param name="nextAfter">Gets the next occurrence strictly after the supplied instant.</param>
    /// <param name="last">The last occurrence the loop ran, or <see langword="null" /> when none was recorded.</param>
    /// <param name="basis">The instant the plan is computed for.</param>
    /// <param name="policy">Decides whether a missed occurrence within its window fires one catch-up pass.</param>
    /// <param name="cancellationToken">A token that stops the evaluation.</param>
    /// <returns>The catch-up occurrence, the next occurrence, and how many were missed.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="nextAfter" /> or <paramref name="policy" /> is <see langword="null" />.
    /// </exception>
    internal static async ValueTask<RecurringOccurrencePlan> PlanAsync(
        Func<DateTimeOffset, CancellationToken, ValueTask<DateTimeOffset?>> nextAfter,
        DateTimeOffset? last,
        DateTimeOffset basis,
        RecurringMissedOccurrencePolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nextAfter);
        ArgumentNullException.ThrowIfNull(policy);

        if (last is null)
        {
            // No history proves nothing was missed: a first run waits for the next occurrence after now.
            var first = await nextAfter(basis, cancellationToken).ConfigureAwait(false);

            return new RecurringOccurrencePlan(null, first, 0, false);
        }

        var occurrence = await nextAfter(last.Value, cancellationToken).ConfigureAwait(false);

        if (occurrence is null || occurrence.Value > basis)
        {
            return new RecurringOccurrencePlan(null, occurrence, 0, false);
        }

        var missed = 1;
        var latest = occurrence.Value;
        DateTimeOffset? next = null;
        var truncated = false;

        while (true)
        {
            var following = await nextAfter(latest, cancellationToken).ConfigureAwait(false);

            if (following is null || following.Value <= latest)
            {
                // The schedule ended, or it broke its strictly-after contract; either way there is nothing
                // further to count, and ending here keeps a misbehaving schedule from spinning the evaluation.
                break;
            }

            if (following.Value > basis)
            {
                next = following;
                break;
            }

            missed++;
            latest = following.Value;

            if (missed >= MaxCountedMissedOccurrences)
            {
                // An outage-scale backlog: replaying it occurrence by occurrence would run hundreds of passes
                // for stale instants, so the loop skips to the next occurrence after now without a catch-up.
                truncated = true;
                next = await nextAfter(basis, cancellationToken).ConfigureAwait(false);
                break;
            }
        }

        if (!truncated
            && policy is RecurringMissedOccurrencePolicy.FireOnce fire
            && basis - latest <= fire.CatchupWindow)
        {
            return new RecurringOccurrencePlan(latest, next, missed, false);
        }

        return new RecurringOccurrencePlan(null, next, missed, truncated);
    }
}
