using System;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Overrides the audit capture mode for one message.
/// </summary>
/// <remarks>
///     <para>
///         The auditing builder sets the capture for every audited message. A message that needs a different timing
///         declares one here through an <c>IAuditCaptureDefinition&lt;TMessage&gt;</c> in a definition class beside
///         the message. There is no attribute form, because an <see cref="AuditCapture" /> is not an
///         attribute-compatible constant.
///     </para>
///     <para>
///         A message with no override is captured with the builder's capture. A message that is not audited ignores
///         this declaration.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class PlaceOrderCommandCaptureDefinition : IAuditCaptureDefinition<PlaceOrderCommand>
/// {
///     public AuditCaptureDeclaration Capture => new(AuditCapture.BeforeAndOutcome());
/// }
/// ]]></code>
/// </example>
public sealed record AuditCaptureDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditCaptureDeclaration" /> class.
    /// </summary>
    /// <param name="capture">The capture mode used for the message instead of the builder's capture.</param>
    /// <exception cref="ArgumentNullException"><paramref name="capture" /> is <see langword="null" />.</exception>
    public AuditCaptureDeclaration(AuditCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        Capture = capture;
    }

    /// <summary>
    ///     Gets the capture mode used for the message instead of the builder's capture.
    /// </summary>
    public AuditCapture Capture { get; }
}
