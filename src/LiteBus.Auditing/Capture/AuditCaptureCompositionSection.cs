using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Capture;

/// <summary>
///     Reports the effective audit capture mode in the composition model.
/// </summary>
/// <remarks>
///     <para>
///         Composition selects <see cref="AuditCapture.AfterCommitThroughOutbox" /> when the audit store cannot join
///         the business transaction. The choice is recorded here rather than applied silently, so the exported
///         composition snapshot shows what each deployment captures with and why.
///     </para>
///     <para>
///         Per-message overrides travel in the per-message declaration records; this section reports the builder
///         default and the store adjustment that applies to it. When no storage package was added, the store reads
///         as unregistered with no join capability.
///     </para>
/// </remarks>
internal sealed class AuditCaptureCompositionSection : ICompositionSectionContributor
{
    /// <summary>
    ///     The auditing configuration this section reports.
    /// </summary>
    private readonly AuditingOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditCaptureCompositionSection" /> class.
    /// </summary>
    /// <param name="options">The auditing configuration.</param>
    public AuditCaptureCompositionSection(AuditingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public string SectionName => "audit-capture";

    /// <inheritdoc />
    public object? GetData()
    {
        var resolution = AuditCaptureResolver.ResolveDefault(_options.Capture, _options.StoreCapabilities);

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["capture"] = Describe(resolution.Requested),
            ["effectiveCapture"] = Describe(resolution.Effective),
            ["effectiveLink"] = AuditCaptureValidator.EffectiveLink(resolution.Effective).ToString(),
            ["storeRegistered"] = _options.StoreCapabilities is not null,
            ["storeJoinsBusinessTransaction"] = _options.StoreCapabilities?.JoinsBusinessTransaction ?? false,
            ["outboxSelectedBecauseStoreCannotJoinTransaction"] =
                resolution.OutboxSelectedBecauseStoreCannotJoinTransaction
        };
    }

    /// <summary>
    ///     Describes a capture mode with the stable names the composition snapshot stores.
    /// </summary>
    /// <param name="capture">The capture mode to describe.</param>
    /// <returns>The stable description, such as <c>WithChange(AfterCommit)</c>.</returns>
    private static string Describe(AuditCapture capture)
    {
        if (capture is AuditCapture.WithChangeCapture withChange)
        {
            return $"WithChange({withChange.Link})";
        }

        if (capture is AuditCapture.AfterCommitThroughOutboxCapture)
        {
            return "AfterCommitThroughOutbox";
        }

        if (capture is AuditCapture.BeforeAndOutcomeCapture)
        {
            return "BeforeAndOutcome";
        }

        return capture.GetType().Name;
    }
}
