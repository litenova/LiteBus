namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares one piece of metadata for messages of type <typeparamref name="TMessage" />.
/// </summary>
/// <typeparam name="TMessage">The message type this definition describes.</typeparam>
/// <typeparam name="TValue">The metadata value type this definition contributes, which is also its metadata key.</typeparam>
/// <remarks>
///     <para>
///         Definitions are the compile-checked alternative to attributes. A definition class lives beside the message it
///         describes, so a feature folder holds the command, its handler, its validator, and its definition together.
///     </para>
///     <para>
///         Definitions are keyed by <typeparamref name="TValue" />, so one class may declare several without being
///         forced to implement the ones it does not need:
///     </para>
///     <code>
/// public sealed class PlaceOrderCommandDefinition :
///     IAuditDefinition&lt;PlaceOrderCommand&gt;,
///     IPermissionDefinition&lt;PlaceOrderCommand&gt;
/// {
///     public AuditDeclaration Audit =&gt; AuditDeclaration.Audited("orders.place-order");
///     public RequiredPermission Required =&gt; Permissions.Orders.Place;
/// }
///     </code>
///     <para>
///         Because the key is the value type, applications may declare their own definitions over their own value types,
///         and the registry applies them without knowing what they mean.
///         <c>IPermissionDefinition&lt;TMessage&gt;</c> above is such a case: it lives in the application, not in
///         LiteBus, and is read back through <see cref="IMessageMetadata" /> by an application pre-stage handler.
///     </para>
///     <para>
///         A definition applies to <typeparamref name="TMessage" /> and to every message assignable to it, so a
///         definition declared over a base type or a marker interface covers the messages beneath it. The most derived
///         declaration wins, which matches how attributes are inherited and how the registry resolves indirect handlers.
///     </para>
///     <para>
///         Two definitions declaring the same value type for the same message are a configuration error and are reported
///         at registration, rather than one of them silently winning.
///     </para>
///     <para>
///         Definition types are instantiated during registration and must expose a parameterless constructor, which may
///         be non-public. They are declarative, so they cannot take dependencies.
///     </para>
/// </remarks>
public interface IMessageDefinition<TMessage, out TValue> : IMessageDefinition
    where TMessage : notnull
    where TValue : notnull
{
    /// <summary>
    ///     Gets the metadata value contributed by this definition.
    /// </summary>
    TValue Value { get; }
}
