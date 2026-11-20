using System.Threading;
using LiteBus.Auditing.Abstractions;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Engine;

/// <summary>
///     Writes one audit record through its capture mode.
/// </summary>
internal interface IAuditCaptureCoordinator
{
    /// <summary>
    ///     Captures one audit record: with the business change, through the outbox, or as intent plus outcome.
    /// </summary>
    /// <param name="record">The audit record to capture.</param>
    /// <param name="capture">The capture mode to capture it with.</param>
    /// <param name="cancellationToken">The token used to cancel the capture.</param>
    /// <returns>A task representing the asynchronous capture.</returns>
    ValueTask CaptureAsync(AuditRecord record, AuditCapture capture, CancellationToken cancellationToken);
}
