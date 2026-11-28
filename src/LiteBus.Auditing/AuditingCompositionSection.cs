using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using LiteBus.Auditing.Capture;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Linking;
using LiteBus.Auditing.Recording;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Auditing;

/// <summary>
///     Reports the effective auditing composition: the capture mode, the chain pieces, and the store they run on.
/// </summary>
/// <remarks>
///     <para>
///         The section snapshots the auditing options while the module builds. When the store cannot join the
///         business transaction, composition sets a requested WithChange capture aside and selects
///         AfterCommitThroughOutbox instead; the requested mode, the effective mode, and the reason are all
///         reported here rather than applied silently.
///     </para>
///     <para>
///         The value <see cref="GetData" /> returns holds ordinal string keys, lists, and primitives, so the
///         composition export stays deterministic.
///     </para>
/// </remarks>
public sealed class AuditingCompositionSection : ICompositionSectionContributor
{
    /// <summary>
    ///     The capture mode the auditing builder set. Snapshot at construction.
    /// </summary>
    private readonly string _requestedCapture;

    /// <summary>
    ///     The capture mode records are captured with. Snapshot at construction.
    /// </summary>
    private readonly string _effectiveCapture;

    /// <summary>
    ///     How the effective capture mode was selected. Snapshot at construction.
    /// </summary>
    private readonly string _captureSelection;

    /// <summary>
    ///     The linking mode the effective capture mode links with. Snapshot at construction.
    /// </summary>
    private readonly string _link;

    /// <summary>
    ///     Whether linking applies: linking applies only with the hash chain on. Snapshot at construction.
    /// </summary>
    private readonly bool _linkApplies;

    /// <summary>
    ///     Whether the hash chain is on. Snapshot at construction.
    /// </summary>
    private readonly bool _chainEnabled;

    /// <summary>
    ///     The chain store type, or <see langword="null" /> when no chain store is registered. Snapshot at construction.
    /// </summary>
    private readonly string? _store;

    /// <summary>
    ///     The backend trail type, or <see langword="null" /> when no trail is registered. Snapshot at construction.
    /// </summary>
    private readonly string? _trail;

    /// <summary>
    ///     The stored document type, or <see langword="null" /> when none resolved. Snapshot at construction.
    /// </summary>
    private readonly string? _document;

    /// <summary>
    ///     The record mapper type, or <see langword="null" /> when none resolved. Snapshot at construction.
    /// </summary>
    private readonly string? _mapper;

    /// <summary>
    ///     The segmenter type, or <see langword="null" /> when none resolved. Snapshot at construction.
    /// </summary>
    private readonly string? _segmenter;

    /// <summary>
    ///     The scheme new records are written with, or <see langword="null" /> when the chain is off. Snapshot at construction.
    /// </summary>
    private readonly string? _scheme;

    /// <summary>
    ///     The checkpoint policy description, or <see langword="null" /> when the chain is off. Snapshot at construction.
    /// </summary>
    private readonly string? _checkpointPolicy;

    /// <summary>
    ///     The checkpoint signer types. Snapshot at construction.
    /// </summary>
    private readonly IReadOnlyList<string> _signers;

    /// <summary>
    ///     The checkpoint publisher types. Snapshot at construction.
    /// </summary>
    private readonly IReadOnlyList<string> _publishers;

    /// <summary>
    ///     The checkpoint source types, with the store's own source first. Snapshot at construction.
    /// </summary>
    private readonly IReadOnlyList<string> _sources;

    /// <summary>
    ///     The age in seconds past which an unlinked record is reported, or <see langword="null" /> when the chain is off. Snapshot at construction.
    /// </summary>
    private readonly double? _unlinkedAfterSeconds;

    /// <summary>
    ///     The age in seconds past which a last checkpoint is stale, or <see langword="null" /> when staleness is never reported. Snapshot at construction.
    /// </summary>
    private readonly double? _checkpointStaleAfterSeconds;

    /// <summary>
    ///     Whether erasure publishes an erasure notice, or <see langword="null" /> when the chain is off. Snapshot at construction.
    /// </summary>
    private readonly bool? _erasureNotices;

    /// <summary>
    ///     The link sweep settings, read when the section is rendered so a change before startup shows.
    /// </summary>
    private readonly AuditLinkSweepHostOptions _linkSweep;

    /// <summary>
    ///     The checkpoint publish settings, read when the section is rendered so a change before startup shows.
    /// </summary>
    private readonly CheckpointPublishHostOptions _checkpointPublishing;

    /// <summary>
    ///     The record-production settings, or <see langword="null" /> when the section was built without them.
    /// </summary>
    private readonly AuditRecordingOptions? _recording;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditingCompositionSection" /> class.
    /// </summary>
    /// <param name="options">The auditing configuration to snapshot.</param>
    /// <param name="resolution">The requested and effective capture modes.</param>
    /// <param name="checkpointPolicy">The checkpoint policy description, or <see langword="null" /> when the chain is off.</param>
    /// <param name="recording">The record-production settings: axes and record writer.</param>
    internal AuditingCompositionSection(
        AuditingOptions options,
        AuditCaptureResolution resolution,
        string? checkpointPolicy,
        AuditRecordingOptions? recording = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolution);

        _requestedCapture = CaptureName(resolution.Requested);
        _effectiveCapture = CaptureName(resolution.Effective);
        _captureSelection = resolution.OutboxSelectedBecauseStoreCannotJoinTransaction
            ? "automatic: the audit store cannot join the business transaction"
            : "configured";
        _link = AuditCaptureValidator.EffectiveLink(resolution.Effective).ToString();
        _chainEnabled = options.Chain is not null;
        _linkApplies = _chainEnabled;
        _store = TypeName(options.ChainStoreType);
        _trail = TypeName(options.TrailType);
        _document = TypeName(options.Chain?.DocumentType ?? options.StoreDefaultDocumentType);
        _mapper = TypeName(options.Chain?.MapperType ?? options.StoreDefaultMapperType);
        _segmenter = TypeName(options.Chain?.SegmenterType ?? options.StoreDefaultSegmenterType);
        _scheme = options.Chain?.SchemeName;
        _checkpointPolicy = checkpointPolicy;
        _signers = TypeNames(options.Chain?.SignerTypes);
        _publishers = TypeNames(options.Chain?.PublisherTypes);
        _sources = TypeNames(options.Chain?.SourceTypes);
        _unlinkedAfterSeconds = options.Chain?.UnlinkedAfter.TotalSeconds;
        _checkpointStaleAfterSeconds = options.Chain?.CheckpointStaleAfter?.TotalSeconds;
        _erasureNotices = options.Chain?.PublishErasureNotices;
        _linkSweep = options.LinkSweep;
        _checkpointPublishing = options.CheckpointPublishing;
        _recording = recording;
    }

    /// <inheritdoc />
    public string SectionName => "auditing";

    /// <inheritdoc />
    public object? GetData()
    {
        SortedDictionary<string, object?> data = new(StringComparer.Ordinal)
        {
            ["captureEffective"] = _effectiveCapture,
            ["captureRequested"] = _requestedCapture,
            ["captureSelection"] = _captureSelection,
            ["chainEnabled"] = _chainEnabled,
            ["checkpointPolicy"] = _checkpointPolicy,
            ["checkpointStaleAfterSeconds"] = _checkpointStaleAfterSeconds,
            ["document"] = _document,
            ["erasureNotices"] = _erasureNotices,
            ["link"] = _link,
            ["linkApplies"] = _linkApplies,
            ["mapper"] = _mapper,
            ["publishers"] = _publishers,
            ["scheme"] = _scheme,
            ["segmenter"] = _segmenter,
            ["signers"] = _signers,
            ["sources"] = _sources,
            ["store"] = _store,
            ["trail"] = _trail,
            ["checkpointPublishingEnabled"] = _checkpointPublishing.Enabled,
            ["linkSweepEnabled"] = _linkSweep.Enabled,
            ["unlinkedAfterSeconds"] = _unlinkedAfterSeconds,
        };

        if (_recording is not null)
        {
            data["axes"] = Axes(_recording);
            data["recordWriter"] = _recording.RecordWriterDescription ?? "built-in";
        }

        return data;
    }

    /// <summary>
    ///     Names the axes that produce audit records.
    /// </summary>
    /// <param name="recording">The record-production settings.</param>
    /// <returns>The axis names, in command, query, event order.</returns>
    private static List<string> Axes(AuditRecordingOptions recording)
    {
        List<string> axes = [];

        if (recording.Commands)
        {
            axes.Add("commands");
        }

        if (recording.Queries)
        {
            axes.Add("queries");
        }

        if (recording.Events)
        {
            axes.Add("events");
        }

        return axes;
    }

    /// <summary>
    ///     Names a capture mode by the factory that creates it.
    /// </summary>
    /// <param name="capture">The capture mode.</param>
    /// <returns>The factory name, such as WithChange.</returns>
    private static string CaptureName(AuditCapture capture)
    {
        return capture switch
        {
            AuditCapture.WithChangeCapture => nameof(AuditCapture.WithChange),
            AuditCapture.AfterCommitThroughOutboxCapture => nameof(AuditCapture.AfterCommitThroughOutbox),
            AuditCapture.BeforeAndOutcomeCapture => nameof(AuditCapture.BeforeAndOutcome),
            _ => capture.GetType().Name,
        };
    }

    /// <summary>
    ///     Names the types a chain option holds.
    /// </summary>
    /// <param name="types">The types, or <see langword="null" /> when the chain is off.</param>
    /// <returns>The type names.</returns>
    private static List<string> TypeNames(IReadOnlyList<Type>? types)
    {
        if (types is null || types.Count == 0)
        {
            return [];
        }

        List<string> names = new(types.Count);

        foreach (var type in types)
        {
            names.Add(TypeName(type) ?? type.Name);
        }

        return names;
    }

    /// <summary>
    ///     Names a type by its full name.
    /// </summary>
    /// <param name="type">The type, or <see langword="null" /> when none is configured.</param>
    /// <returns>The full type name, or <see langword="null" /> when no type is configured.</returns>
    private static string? TypeName(Type? type)
    {
        return type?.FullName ?? type?.Name;
    }
}
