namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares the audit position of <typeparamref name="TMessage" /> from a definition class beside the message.
/// </summary>
/// <typeparam name="TMessage">The message type this definition describes.</typeparam>
/// <remarks>
///     <para>
///         This is the compile-checked alternative to <see cref="AuditedAttribute" /> and
///         <see cref="AuditExemptAttribute" />. Implementing it forces the message to state its position, and the
///         declaration is written in ordinary C# where constants and shared vocabulary are available.
///     </para>
///     <para>
///         A definition takes precedence over an attribute declaring the same message, because both contribute an
///         <see cref="AuditDeclaration" /> and definitions are applied after attributes.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class PlaceOrderCommandDefinition : IAuditDefinition<PlaceOrderCommand>
/// {
///     public AuditDeclaration Audit => AuditDeclaration.Audited(AuditActions.Orders.Place) with
///     {
///         Category = AuditCategories.Money,
///         TargetKind = "order"
///     };
/// }
/// ]]></code>
/// </example>
public interface IAuditDefinition<TMessage> : IMessageDefinition<TMessage, AuditDeclaration>
    where TMessage : notnull
{
    /// <summary>
    ///     Gets the audit declaration for <typeparamref name="TMessage" />.
    /// </summary>
    AuditDeclaration Audit { get; }

    /// <inheritdoc />
    AuditDeclaration IMessageDefinition<TMessage, AuditDeclaration>.Value => Audit;
}
