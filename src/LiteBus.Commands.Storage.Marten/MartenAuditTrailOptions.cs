using System;

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
///         to its predecessor with a hash, so a review can prove that no record was removed, reordered, or altered.
///         The builder configures every part of the chain: the stored document, the segmenting, the hash scheme,
///         when checkpoints are cut, and how they are signed and published. See
///         <see cref="MartenAuditRecordCanonicalizer" /> for the canonical form the default hash covers, and
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
    ///     The default interval between time-based checkpoint scans.
    /// </summary>
    public static readonly TimeSpan DefaultCheckpointScanInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Gets a value indicating whether records are sequenced and hash-chained per segment.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> until <see cref="UseHashChain" /> is called.
    /// </value>
    public bool HashChainEnabled { get; private set; }

    /// <summary>
    ///     Gets or sets how often the checkpoint loop asks a time-based policy for each open segment.
    /// </summary>
    /// <value>
    ///     The default is <see cref="DefaultCheckpointScanInterval" />. Must be positive. Only a chain whose policy
    ///     reports time-based checkpoints runs the loop.
    /// </value>
    public TimeSpan CheckpointScanInterval { get; set; } = DefaultCheckpointScanInterval;

    /// <summary>
    ///     Gets the built hash chain operations, or <see langword="null" /> when <see cref="UseHashChain" /> was
    ///     not called.
    /// </summary>
    internal MartenAuditChainConfiguration? ChainConfiguration { get; private set; }

    /// <summary>
    ///     Enables sequence numbers and the hash chain, optionally configured through the builder.
    /// </summary>
    /// <param name="configure">The hash chain configuration action, or <see langword="null" /> for the defaults.</param>
    /// <returns>The options, so calls can be chained.</returns>
    /// <remarks>
    ///     A chained record is written out of band on its own serialized session even for a successful mediation,
    ///     because the sequence number is assigned under the segment lock at commit time. See
    ///     <see cref="MartenAuditTrail" /> for what that costs.
    /// </remarks>
    public MartenAuditTrailOptions UseHashChain(Action<MartenAuditHashChainBuilder>? configure = null)
    {
        var builder = new MartenAuditHashChainBuilder();
        configure?.Invoke(builder);

        ChainConfiguration = builder.Build();
        HashChainEnabled = true;

        return this;
    }

    /// <summary>
    ///     Returns the built hash chain operations, failing when the chain is not enabled.
    /// </summary>
    /// <returns>The built hash chain operations.</returns>
    /// <exception cref="InvalidOperationException"><see cref="UseHashChain" /> was not called.</exception>
    internal MartenAuditChainConfiguration RequireChain()
    {
        if (ChainConfiguration is not { } chain)
        {
            throw new InvalidOperationException(
                "The audit hash chain is not enabled. Call MartenAuditTrailOptions.UseHashChain to enable it.");
        }

        return chain;
    }

    /// <summary>
    ///     Validates the configured values before the trail is built.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="CheckpointScanInterval" /> is zero or negative.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(CheckpointScanInterval, TimeSpan.Zero);
    }
}
