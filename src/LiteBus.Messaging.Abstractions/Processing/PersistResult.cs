using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Outcome of a terminal <c>PersistAsync</c> call that writes post-dispatch envelope state.
/// </summary>
/// <remarks>
///     Stores return explicit applied and skipped counts so processors can detect lease-lost no-ops
///     without inferring success from a silent zero-row update.
/// </remarks>
public sealed record PersistResult
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PersistResult" /> class.
    /// </summary>
    /// <param name="appliedCount">The number of envelopes whose terminal state was written.</param>
    /// <param name="skippedCount">The number of envelopes skipped because the lease was lost or invalid.</param>
    public PersistResult(int appliedCount, int skippedCount)
    {
        if (appliedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appliedCount), appliedCount, "Applied count cannot be negative.");
        }

        if (skippedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skippedCount), skippedCount, "Skipped count cannot be negative.");
        }

        AppliedCount = appliedCount;
        SkippedCount = skippedCount;
    }

    /// <summary>
    ///     Gets the number of envelopes whose terminal state was written.
    /// </summary>
    /// <value>The count of envelopes persisted by the store.</value>
    public int AppliedCount { get; }

    /// <summary>
    ///     Gets the number of envelopes skipped because the active lease was lost or no longer matched.
    /// </summary>
    /// <value>The count of envelopes that were not persisted.</value>
    public int SkippedCount { get; }

    /// <summary>
    ///     Gets an empty result with zero applied and skipped envelopes.
    /// </summary>
    /// <value>A result representing a no-op persist call.</value>
    public static PersistResult Empty { get; } = new(0, 0);

    /// <summary>
    ///     Creates a result where every supplied envelope was persisted.
    /// </summary>
    /// <param name="count">The number of envelopes applied.</param>
    /// <returns>A persist result with the supplied applied count and zero skipped envelopes.</returns>
    public static PersistResult AllApplied(int count)
    {
        return new PersistResult(count, 0);
    }

    /// <summary>
    ///     Creates a result from applied and skipped counts.
    /// </summary>
    /// <param name="appliedCount">The number of envelopes persisted.</param>
    /// <param name="skippedCount">The number of envelopes skipped.</param>
    /// <returns>A persist result with the supplied counts.</returns>
    public static PersistResult FromOutcome(int appliedCount, int skippedCount)
    {
        return new PersistResult(appliedCount, skippedCount);
    }

    /// <summary>
    ///     Creates a result from the message identifiers supplied to persist and the subset that were written.
    /// </summary>
    /// <param name="messageIds">The message identifiers supplied to persist.</param>
    /// <param name="persistedMessageIds">The identifiers whose terminal state was written.</param>
    /// <returns>A persist result with applied and skipped counts derived from the identifier sets.</returns>
    public static PersistResult FromMessageIds(IReadOnlyList<Guid> messageIds, ISet<Guid> persistedMessageIds)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        ArgumentNullException.ThrowIfNull(persistedMessageIds);

        var applied = 0;

        foreach (var messageId in messageIds)
        {
            if (persistedMessageIds.Contains(messageId))
            {
                applied++;
            }
        }

        return new PersistResult(applied, messageIds.Count - applied);
    }
}