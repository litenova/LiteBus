using System;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.Capture;

/// <summary>
///     Validates audit capture modes and reads the linking and outbox behavior each mode implies.
/// </summary>
/// <remarks>
///     <para>
///         A capture mode decides when and where an audit record is written, relative to the business commit.
///         Linking decides when the chain position is assigned, and applies only with the hash chain on. The two
///         settings are independent, but not every pair is valid: <see cref="AuditCapture.WithChange" /> links
///         <see cref="AuditLink.AfterCommit" /> or <see cref="AuditLink.InTransaction" />, while
///         <see cref="AuditCapture.AfterCommitThroughOutbox" /> and <see cref="AuditCapture.BeforeAndOutcome" />
///         always link <see cref="AuditLink.AtWrite" />.
///     </para>
///     <para>
///         Composition rejects any other pair with an <see cref="AuditConfigurationException" />, which is a
///         <see cref="LiteBusConfigurationException" /> naming the audit configuration fault.
///     </para>
/// </remarks>
internal static class AuditCaptureValidator
{
    /// <summary>
    ///     Validates that a capture mode pairs with a linking mode composition accepts.
    /// </summary>
    /// <param name="capture">The capture mode to validate.</param>
    /// <param name="source">Where the capture mode was configured, named in the error when it is invalid.</param>
    /// <exception cref="AuditConfigurationException">The capture and link pair is invalid.</exception>
    public static void Validate(AuditCapture capture, string source)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (capture is AuditCapture.AfterCommitThroughOutboxCapture
            or AuditCapture.BeforeAndOutcomeCapture)
        {
            return;
        }

        if (capture is AuditCapture.WithChangeCapture withChange)
        {
            ValidateLink(withChange.Link, source);
            return;
        }

        throw new AuditConfigurationException(
            $"Audit capture configured by {source} uses an unknown capture mode "
            + $"'{capture.GetType().FullName}'. Use WithChange, AfterCommitThroughOutbox, or BeforeAndOutcome.");
    }

    /// <summary>
    ///     Determines whether records captured with a mode travel through the transactional outbox.
    /// </summary>
    /// <param name="capture">The capture mode to inspect.</param>
    /// <returns>
    ///     <see langword="true" /> for <see cref="AuditCapture.AfterCommitThroughOutbox" />; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    public static bool RequiresOutbox(AuditCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        return capture is AuditCapture.AfterCommitThroughOutboxCapture;
    }

    /// <summary>
    ///     Reads the linking mode records captured with a mode are linked with.
    /// </summary>
    /// <param name="capture">The capture mode to inspect.</param>
    /// <returns>
    ///     The link carried by <see cref="AuditCapture.WithChange" />, or <see cref="AuditLink.AtWrite" /> for the
    ///     other two modes.
    /// </returns>
    /// <exception cref="AuditConfigurationException">The capture mode is unknown.</exception>
    public static AuditLink EffectiveLink(AuditCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        if (capture is AuditCapture.WithChangeCapture withChange)
        {
            return withChange.Link;
        }

        if (capture is AuditCapture.AfterCommitThroughOutboxCapture
            or AuditCapture.BeforeAndOutcomeCapture)
        {
            return AuditLink.AtWrite;
        }

        throw new AuditConfigurationException(
            $"Audit capture uses an unknown capture mode '{capture.GetType().FullName}'. "
            + "Use WithChange, AfterCommitThroughOutbox, or BeforeAndOutcome.");
    }

    /// <summary>
    ///     Validates the linking mode carried by a WithChange capture mode.
    /// </summary>
    /// <param name="link">The linking mode to validate.</param>
    /// <param name="source">Where the capture mode was configured, named in the error when it is invalid.</param>
    /// <exception cref="AuditConfigurationException">
    ///     The linking mode is not one composition accepts with WithChange.
    /// </exception>
    private static void ValidateLink(AuditLink link, string source)
    {
        if (link is AuditLink.AfterCommit or AuditLink.InTransaction)
        {
            return;
        }

        if (link == AuditLink.AtWrite)
        {
            throw new AuditConfigurationException(
                $"Audit capture configured by {source} pairs WithChange with AtWrite linking, which composition "
                + "rejects. WithChange links AfterCommit (the default) or InTransaction; AfterCommitThroughOutbox "
                + "and BeforeAndOutcome always link AtWrite.");
        }

        throw new AuditConfigurationException(
            $"Audit capture configured by {source} pairs WithChange with an unknown linking value '{(int) link}', "
            + "which composition rejects. WithChange links AfterCommit (the default) or InTransaction.");
    }
}
