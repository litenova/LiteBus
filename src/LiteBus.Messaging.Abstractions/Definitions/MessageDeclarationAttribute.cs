using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     States, on an attribute class, which metadata value that attribute declares.
/// </summary>
/// <remarks>
///     <para>
///         Apply it to an attribute implementing <see cref="IMessageDeclarationSource" />, naming the same type that
///         contract's <see cref="IMessageDeclarationSource.DeclarationType" /> returns. Registration verifies the two
///         agree and fails composition when they do not, so the pair cannot drift.
///     </para>
///     <para>
///         It exists because <see cref="IMessageDeclarationSource.DeclarationType" /> is a runtime property and an
///         analyzer cannot execute it. Without a declaration an analyzer can read, no compile-time rule can tell that
///         <c>[RequiresPermission("orders.write")]</c> is how a message states its <c>RequiredPermission</c>, which is
///         what <c>LB1020</c> has to know to report a message that states nothing.
///     </para>
///     <para>
///         A definition class needs no annotation. Its declaration is already in the type system, as the second type
///         argument of <see cref="IMessageDefinition{TMessage,TValue}" />.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// [MessageDeclaration(typeof(RequiredPermission))]
/// [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
/// public sealed class RequiresPermissionAttribute : Attribute, IMessageDeclarationSource
/// {
///     public RequiresPermissionAttribute(string permission) => Permission = permission;
///
///     public string Permission { get; }
///
///     public Type DeclarationType => typeof(RequiredPermission);
///
///     public object CreateDeclaration() => new RequiredPermission(Permission);
/// }
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageDeclarationAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageDeclarationAttribute" /> class.
    /// </summary>
    /// <param name="declarationType">The metadata value type the annotated attribute declares.</param>
    /// <exception cref="ArgumentNullException"><paramref name="declarationType" /> is <see langword="null" />.</exception>
    public MessageDeclarationAttribute(Type declarationType)
    {
        ArgumentNullException.ThrowIfNull(declarationType);
        DeclarationType = declarationType;
    }

    /// <summary>
    ///     Gets the metadata value type the annotated attribute declares.
    /// </summary>
    public Type DeclarationType { get; }
}
