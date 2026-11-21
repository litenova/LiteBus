using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Records the scheduled occurrences a recurring loop has run, so catch-up decisions survive restarts.
/// </summary>
/// <remarks>
///     The store-neutral port behind missed-occurrence catch-up: the loop records each occurrence it runs, and a
///     restarting loop reads the last record to decide whether one catch-up pass is still due.
/// </remarks>
public interface IRecurringOccurrenceStore
{
    /// <summary>
    ///     Tries to record an occurrence.
    /// </summary>
    /// <param name="occurrence">The occurrence to record.</param>
    /// <param name="cancellationToken">A token that stops the write.</param>
    /// <returns>
    ///     <see langword="true" /> when the occurrence was recorded; <see langword="false" /> when its key already
    ///     exists.
    /// </returns>
    ValueTask<bool> TryRecordAsync(RecurringOccurrence occurrence, CancellationToken cancellationToken);

    /// <summary>
    ///     Gets the most recent occurrence recorded for one loop.
    /// </summary>
    /// <param name="loopName">The name of the recurring loop.</param>
    /// <param name="tenantId">The tenant the occurrence belongs to, or <see langword="null" /> for a loop without tenants.</param>
    /// <param name="cancellationToken">A token that stops the read.</param>
    /// <returns>The last recorded occurrence, or <see langword="null" /> when none was recorded.</returns>
    ValueTask<RecurringOccurrence?> GetLastAsync(string loopName, string? tenantId, CancellationToken cancellationToken);
}
