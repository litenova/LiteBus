using System;
using System.Globalization;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Configures the Marten audit trail hash chain, segmenting, and checkpoint publishing.
/// </summary>
/// <remarks>
///     <para>
///         Adjusted by registering a configured instance with the application container: the container-constructed
///         <see cref="MartenAuditTrail" /> takes these options as an optional constructor parameter. Without a
///         registered instance the trail writes plain records exactly as before, with no sequence numbers and no hash
///         chain.
///     </para>
///     <para>
///         Calling <see cref="UseHashChain" /> assigns every record a sequence number inside its segment and links it
///         to its predecessor with a SHA-256 hash, so a review can prove that no record was removed, reordered, or
///         altered. See <see cref="MartenAuditHashChain" /> for the canonical form the hash covers, and
///         <see cref="MartenAuditTrail.VerifyChainAsync" /> for the verification pass.
///     </para>
/// </remarks>
public sealed class MartenAuditTrailOptions
{
    /// <summary>
    ///     The default checkpoint interval: a checkpoint is published every one hundred records of a segment.
    /// </summary>
    public const int DefaultCheckpointInterval = 100;

    /// <summary>
    ///     The category reported for a record that carries none, so the default segment stays stable.
    /// </summary>
    private const string UncategorizedCategory = "uncategorized";

    /// <summary>
    ///     Gets a value indicating whether records are sequenced and hash-chained per segment.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> until <see cref="UseHashChain" /> is called.
    /// </value>
    public bool HashChainEnabled { get; private set; }

    /// <summary>
    ///     Gets the selector that assigns a record to its hash-chain segment.
    /// </summary>
    /// <value>
    ///     <see cref="DefaultSegment" /> until <see cref="UseHashChain" /> replaces it.
    /// </value>
    public Func<AuditRecord, string> SegmentOf { get; private set; } = DefaultSegment;

    /// <summary>
    ///     Gets or sets the publisher that receives a checkpoint every <see cref="CheckpointInterval" /> records.
    /// </summary>
    /// <value>
    ///     <see langword="null" /> by default, which records the chain without publishing checkpoints.
    /// </value>
    public ICheckpointPublisher? CheckpointPublisher { get; set; }

    /// <summary>
    ///     Gets or sets how many records of a segment are written between published checkpoints.
    /// </summary>
    /// <value>
    ///     The default is <see cref="DefaultCheckpointInterval" />. Must be positive. A checkpoint is published when a
    ///     record whose sequence number is an exact multiple of this value commits.
    /// </value>
    public int CheckpointInterval { get; set; } = DefaultCheckpointInterval;

    /// <summary>
    ///     Assigns a record to the segment for its category and year.
    /// </summary>
    /// <param name="record">The record produced at the mediation boundary.</param>
    /// <returns>The segment name in the form <c>{category}/{year}</c>.</returns>
    /// <remarks>
    ///     A record without a category lands in the <c>uncategorized</c> segment for its year, so the selector never
    ///     returns a blank segment.
    /// </remarks>
    public static string DefaultSegment(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var category = string.IsNullOrWhiteSpace(record.Category) ? UncategorizedCategory : record.Category;

        return string.Concat(category, "/", record.OccurredAt.Year.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    ///     Enables sequence numbers and the hash chain, optionally with a custom segment selector.
    /// </summary>
    /// <param name="segmentOf">
    ///     The selector that assigns a record to its segment, or <see langword="null" /> for
    ///     <see cref="DefaultSegment" />.
    /// </param>
    /// <returns>The options, so calls can be chained.</returns>
    /// <remarks>
    ///     A chained record is written out of band on its own serialized session even for a successful mediation,
    ///     because the sequence number is assigned under the segment lock at commit time. See
    ///     <see cref="MartenAuditTrail" /> for what that costs.
    /// </remarks>
    public MartenAuditTrailOptions UseHashChain(Func<AuditRecord, string>? segmentOf = null)
    {
        HashChainEnabled = true;
        SegmentOf = segmentOf ?? DefaultSegment;

        return this;
    }

    /// <summary>
    ///     Validates the configured values before the trail is built.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="CheckpointInterval" /> is zero or negative.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(CheckpointInterval, 1);
    }
}
