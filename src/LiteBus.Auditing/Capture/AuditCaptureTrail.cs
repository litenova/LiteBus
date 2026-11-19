using System;
using LiteBus.Auditing.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Engine;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.Capture;

/// <summary>
///     An audit trail that captures each record with the mode the pre-handler stage resolved for its mediation.
/// </summary>
/// <remarks>
///     <para>
///         The record writer observes only the built record, whose message type name cannot be resolved back to a
///         type, so the per-message capture override is resolved while the message type is still known and handed
///         forward in the mediation data. This trail reads it back and hands the record and the mode to the capture
///         coordinator together.
///     </para>
///     <para>
///         A record written with no mediation in flight, or for a message the pre-handler stage never saw, is
///         captured with the builder default.
///     </para>
/// </remarks>
internal sealed class AuditCaptureTrail : IAuditTrail
{
    /// <summary>
    ///     Writes one record through its capture mode.
    /// </summary>
    private readonly IAuditCaptureCoordinator _coordinator;

    /// <summary>
    ///     Captures records the pre-handler stage resolved nothing for.
    /// </summary>
    private readonly AuditCapture _defaultCapture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditCaptureTrail" /> class.
    /// </summary>
    /// <param name="coordinator">The coordinator that writes one record through its capture mode.</param>
    /// <param name="defaultCapture">The capture mode the auditing builder set.</param>
    /// <exception cref="AuditConfigurationException">The builder default pairs an invalid capture and link.</exception>
    public AuditCaptureTrail(IAuditCaptureCoordinator coordinator, AuditCapture defaultCapture)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(defaultCapture);

        AuditCaptureValidator.Validate(defaultCapture, "the auditing builder");

        _coordinator = coordinator;
        _defaultCapture = defaultCapture;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var executionContext = AmbientExecutionContext.GetCurrentOrDefault();
        var capture = AuditCaptureResolver.ReadResolvedCapture(executionContext) ?? _defaultCapture;

        await _coordinator.CaptureAsync(record, capture, cancellationToken).ConfigureAwait(false);
    }
}
