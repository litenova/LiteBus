using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Records a refusal that reached a <c>Try</c> mediator method as an exception the pipeline did not decide.
/// </summary>
/// <remarks>
///     <para>
///         A <c>Try</c> method reads its outcome from the <see cref="MediationEndingCapture" /> the pipeline writes when a
///         guard, validator, or fault classifier refuses the message. A refusal can also arrive from further away: a
///         handler that sends a guarded command, or one that throws <see cref="LiteBusMessageDeniedException" /> itself.
///         Nothing records that ending, so the capture still says <see cref="MediationOutcome.Succeeded" />, and
///         returning it would report success with a default value.
///     </para>
///     <para>
///         The reason and code are derived the way the pipeline derives them for its own decisions, so a caller
///         branching on them sees the same shape either way.
///     </para>
/// </remarks>
internal static class EscapedRefusal
{
    /// <summary>
    ///     Records the refusal an exception carries when the capture holds no refusal of its own.
    /// </summary>
    /// <param name="capture">The capture the <c>Try</c> method installed.</param>
    /// <param name="exception">The refusal exception that ended the mediation.</param>
    public static void Record(MediationEndingCapture capture, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(exception);

        if (capture.Outcome is MediationOutcome.Denied or MediationOutcome.Invalid)
        {
            return;
        }

        switch (exception)
        {
            case LiteBusMessageDeniedException denied:
                capture.Outcome = MediationOutcome.Denied;
                capture.Reason = denied.Reason ?? denied.Message;
                capture.Code = denied.Code;
                capture.Failures = [];
                capture.DecidedBy = denied.DeniedBy;
                break;

            case LiteBusMessageInvalidException invalid:
                capture.Outcome = MediationOutcome.Invalid;
                capture.Reason = invalid.Failures.Count > 0
                    ? string.Join("; ", invalid.Failures.Select(failure => failure.ToString()))
                    : invalid.Message;
                capture.Code = invalid.Failures.Count == 1 ? invalid.Failures[0].Code : null;
                capture.Failures = invalid.Failures;
                capture.DecidedBy = invalid.RejectedBy;
                break;
        }
    }
}
