using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing;

/// <summary>
///     The hash chain configuration an <see cref="AuditHashChainBuilder" /> recorded.
/// </summary>
/// <remarks>
///     Builder methods write these values; the chain engine reads them. Every piece has a default, so a chain that
///     needs nothing special writes records with <see cref="DefaultSchemeName" />, cuts a checkpoint every one
///     hundred records, and reports an unlinked record past <see cref="DefaultUnlinkedAfter" />.
/// </remarks>
public sealed class AuditHashChainOptions
{
    /// <summary>
    ///     Gets the scheme new records are written with unless <see cref="AuditHashChainBuilder.UseScheme" /> names
    ///     another.
    /// </summary>
    public const string DefaultSchemeName = "sha256-v2";

    /// <summary>
    ///     Gets the age past which an unlinked record is reported unless
    ///     <see cref="AuditHashChainBuilder.UnlinkedAfter" /> names another.
    /// </summary>
    public static readonly TimeSpan DefaultUnlinkedAfter = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Gets the stored document type, or <see langword="null" /> when no explicit type was configured
    ///     and the storage default applies.
    /// </summary>
    public Type? DocumentType { get; internal set; }

    /// <summary>
    ///     Gets the record mapper type, or <see langword="null" /> when no explicit type was configured and
    ///     the storage default applies.
    /// </summary>
    public Type? MapperType { get; internal set; }

    /// <summary>
    ///     Gets the segmenter type, or <see langword="null" /> when no explicit type was configured and the
    ///     storage default applies.
    /// </summary>
    public Type? SegmenterType { get; internal set; }

    /// <summary>
    ///     Gets the scheme new records are written with.
    /// </summary>
    public string SchemeName { get; internal set; } = DefaultSchemeName;

    /// <summary>
    ///     Gets the checkpoint policy instance, or <see langword="null" /> when the policy comes from
    ///     <see cref="CheckpointPolicyType" /> or the default applies.
    /// </summary>
    public ICheckpointPolicy? CheckpointPolicy { get; internal set; }

    /// <summary>
    ///     Gets the checkpoint policy type resolved from the container, or <see langword="null" /> when the
    ///     policy comes from <see cref="CheckpointPolicy" /> or the default applies.
    /// </summary>
    public Type? CheckpointPolicyType { get; internal set; }

    /// <summary>
    ///     Gets the checkpoint signer types. Every signer signs every cut checkpoint, so signatures coexist.
    /// </summary>
    public IReadOnlyList<Type> SignerTypes { get; internal set; } = [];

    /// <summary>
    ///     Gets the checkpoint publisher types. Every publisher receives every cut checkpoint.
    /// </summary>
    public IReadOnlyList<Type> PublisherTypes { get; internal set; } = [];

    /// <summary>
    ///     Gets the extra checkpoint source types verification compares against. The store's own source is
    ///     always first.
    /// </summary>
    public IReadOnlyList<Type> SourceTypes { get; internal set; } = [];

    /// <summary>
    ///     Gets the age past which a record left unlinked is reported as an
    ///     <see cref="AuditChainBreakKind.Unlinked" /> break.
    /// </summary>
    public TimeSpan UnlinkedAfter { get; internal set; } = DefaultUnlinkedAfter;

    /// <summary>
    ///     Gets the age past which a last checkpoint with records after it is reported as a
    ///     <see cref="AuditChainBreakKind.CheckpointStale" /> break, or <see langword="null" /> when staleness is
    ///     never reported.
    /// </summary>
    public TimeSpan? CheckpointStaleAfter { get; internal set; }

    /// <summary>
    ///     Gets a value indicating whether erasure publishes an erasure notice carrying no hash.
    /// </summary>
    public bool PublishErasureNotices { get; internal set; }
}
