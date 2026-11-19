using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Declares the audit capture mode of <typeparamref name="TMessage" /> from a definition class beside the message.
/// </summary>
/// <typeparam name="TMessage">The message type this definition describes.</typeparam>
/// <remarks>
///     <para>
///         The auditing builder sets the capture for every audited message. Implement this only for a message that
///         needs a different timing, such as a command whose attempt must be recorded before its handler runs. A
///         message with no definition is captured with the builder's capture.
///     </para>
///     <para>
///         There is no attribute form of this declaration, because an <see cref="AuditCapture" /> is not an
///         attribute-compatible constant.
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
public interface IAuditCaptureDefinition<TMessage> : IMessageDefinition<TMessage, AuditCaptureDeclaration>
    where TMessage : notnull
{
    /// <summary>
    ///     Gets the audit capture declaration for <typeparamref name="TMessage" />.
    /// </summary>
    AuditCaptureDeclaration Capture { get; }

    /// <inheritdoc />
    AuditCaptureDeclaration IMessageDefinition<TMessage, AuditCaptureDeclaration>.Value => Capture;
}
