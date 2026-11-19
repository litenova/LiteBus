using System;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing;

/// <summary>
///     The auditing configuration an <see cref="AuditingModuleBuilder" /> recorded.
/// </summary>
/// <remarks>
///     Builder methods write these values; the auditing module and the engine read them. A storage package fills the
///     store entries through its extension on <see cref="AuditingModuleBuilder" />.
/// </remarks>
public sealed class AuditingOptions
{
    /// <summary>
    ///     Gets when and where audit records are written, relative to the business commit.
    /// </summary>
    /// <value>
    ///     The capture mode. Defaults to <see cref="AuditCapture.WithChange" /> with
    ///     <see cref="AuditLink.AfterCommit" /> linking. Composition picks
    ///     <see cref="AuditCapture.AfterCommitThroughOutbox" /> when the store cannot join the business
    ///     transaction, visibly in the composition model.
    /// </value>
    public AuditCapture Capture { get; internal set; } = AuditCapture.WithChange();

    /// <summary>
    ///     Gets the hash chain configuration, or <see langword="null" /> when the chain is off.
    /// </summary>
    public AuditHashChainOptions? Chain { get; internal set; }

    /// <summary>
    ///     Gets the audit trail implementation type registered by the storage package, or
    ///     <see langword="null" /> when no storage package was added.
    /// </summary>
    public Type? TrailType { get; internal set; }

    /// <summary>
    ///     Gets the chain store implementation type registered by the storage package, or
    ///     <see langword="null" /> when no storage package was added.
    /// </summary>
    public Type? ChainStoreType { get; internal set; }

    /// <summary>
    ///     Gets what the registered audit store can do, or <see langword="null" /> when no storage package
    ///     was added.
    /// </summary>
    public AuditStoreCapabilities? StoreCapabilities { get; internal set; }

    /// <summary>
    ///     Gets the default stored document type supplied by the storage package, or
    ///     <see langword="null" /> when the package supplies none.
    /// </summary>
    /// <remarks>
    ///     An explicit <see cref="AuditHashChainBuilder.StoreAs{TDocument, TMapper}" /> call always wins over this
    ///     default, regardless of call order.
    /// </remarks>
    public Type? StoreDefaultDocumentType { get; internal set; }

    /// <summary>
    ///     Gets the default record mapper type supplied by the storage package, or
    ///     <see langword="null" /> when the package supplies none.
    /// </summary>
    /// <remarks>
    ///     An explicit <see cref="AuditHashChainBuilder.StoreAs{TDocument, TMapper}" /> call always wins over this
    ///     default, regardless of call order.
    /// </remarks>
    public Type? StoreDefaultMapperType { get; internal set; }

    /// <summary>
    ///     Gets the default segmenter type supplied by the storage package, or
    ///     <see langword="null" /> when the package supplies none.
    /// </summary>
    /// <remarks>
    ///     An explicit <see cref="AuditHashChainBuilder.SegmentBy{TSegmenter}" /> call always wins over this
    ///     default, regardless of call order.
    /// </remarks>
    public Type? StoreDefaultSegmenterType { get; internal set; }

    /// <summary>
    ///     Gets the default canonicalizer type supplied by the storage package for its own document, or
    ///     <see langword="null" /> when the package supplies none.
    /// </summary>
    /// <remarks>
    ///     The shipped hash schemes need a canonicalizer for the stored document. The default applies only while the
    ///     chain stores the package's own document; an application that stores its own document with
    ///     <see cref="AuditHashChainBuilder.StoreAs{TDocument, TMapper}" /> registers its own canonicalizer.
    /// </remarks>
    public Type? StoreDefaultCanonicalizerType { get; internal set; }
}
