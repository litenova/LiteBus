namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares one facet of metadata for messages of type <typeparamref name="TMessage" />.
/// </summary>
/// <typeparam name="TMessage">The message type this facet describes.</typeparam>
/// <typeparam name="TValue">The metadata value type this facet contributes, which is also its metadata key.</typeparam>
/// <remarks>
///     <para>
///         Definitions are the compile-checked alternative to attributes. A definition class lives beside the message it
///         describes, so a feature folder holds the command, its handler, its validator, and its definition together.
///     </para>
///     <para>
///         Facets are segregated by <typeparamref name="TValue" />, so one class may declare several facets without
///         being forced to implement the ones it does not need:
///     </para>
///     <code>
/// public sealed class PlaceOrderCommandDefinition :
///     IAuditDefinition&lt;PlaceOrderCommand&gt;,
///     IPermissionDefinition&lt;PlaceOrderCommand&gt;
/// {
///     public AuditDeclaration Audit => AuditDeclaration.Audited("orders.place-order");
///     public RequiredPermission Required => Permissions.Orders.Place;
/// }
///     </code>
///     <para>
///         Because the facet is keyed by <typeparamref name="TValue" />, applications may declare their own facets over
///         their own value types, and the registry applies them without knowing what they mean.
///         <c>IPermissionDefinition&lt;TMessage&gt;</c> above is such a facet: it lives in the application, not in
///         LiteBus, and is read back through <see cref="IMessageMetadata" /> by an application pre-handler.
///     </para>
///     <para>
///         Definition types are instantiated during registration and must expose a parameterless constructor, which may
///         be non-public. The order in which definitions are applied is undefined, so a definition must not depend on
///         another definition having run first.
///     </para>
/// </remarks>
public interface IMessageDefinition<TMessage, out TValue> : IMessageDefinition
    where TMessage : notnull
    where TValue : notnull
{
    /// <summary>
    ///     Gets the metadata value contributed by this facet.
    /// </summary>
    TValue Value { get; }
}
