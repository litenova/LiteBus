namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares the metadata for many message types from one type.
/// </summary>
/// <remarks>
///     <para>
///         A definition class describes one message type. A consumer that generates its structure from specifications
///         would otherwise emit one definition class per message per concern, hundreds of nearly identical files. One
///         generated class per module implementing this interface declares every message of that module through the
///         same <see cref="IMessageDeclarations" /> vocabulary.
///     </para>
///     <para>
///         The interface extends the <see cref="IMessageDefinition" /> marker, so existing <c>Register</c> and
///         <c>RegisterFromAssembly</c> discovery finds sources with no new registration API. Sources are instantiated
///         once during registration and must expose a parameterless constructor, which may be non-public. They are
///         declarative, so they cannot take dependencies, and <see cref="Describe" /> must not read configuration or
///         reach for a container.
///     </para>
///     <para>
///         A source value stands where a definition value stands: it wins over an attribute, a default, and a
///         convention, and a conflicting repeat for the same message and value type fails composition naming both
///         sources. An identical repeat is a no-op, and a deliberate override calls
///         <see cref="IMessageDeclarationCatalog.Replace{TMessage}" />.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// internal sealed class BillingDeclarations : IMultiMessageDefinition
/// {
///     public void Describe(IMessageDeclarationCatalog catalog)
///     {
///         catalog
///             .Describe<ChargeCardCommand>(declarations => declarations.Declare(Permissions.Billing.Charge))
///             .Describe<RefundCommand>(declarations => declarations.Declare(Permissions.Billing.Refund));
///     }
/// }
/// ]]></code>
/// </example>
public interface IMultiMessageDefinition : IMessageDefinition
{
    /// <summary>
    ///     Declares the metadata for every message the source covers.
    /// </summary>
    /// <param name="catalog">The catalog that receives every message's declarations.</param>
    void Describe(IMessageDeclarationCatalog catalog);
}
