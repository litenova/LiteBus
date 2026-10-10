using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks an attribute as a source of message metadata, and states which metadata value it declares.
/// </summary>
/// <remarks>
///     <para>
///         Only attributes implement this contract, so the wording below says attribute throughout. The name avoids the
///         <c>Attribute</c> suffix because the interface is not itself an attribute.
///     </para>
///     <para>
///         A message type carries attributes for many reasons that have nothing to do with mediation: serialization,
///         diagnostics, source generators. Collecting all of them into message metadata would make the collection
///         unbounded and unpredictable, and would make <see cref="IMessageMetadata.Contains{TValue}" /> answer questions
///         about types LiteBus never meant to describe. Only attributes that implement this interface are collected.
///     </para>
///     <para>
///         Implementing it also normalizes the two declaration sources onto one key. The attribute is converted to the
///         same value type a definition contributes, so an explicit definition overwrites the attribute rather than
///         sitting beside it, and a reader looks the value up once by its own type.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
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
public interface IMessageDeclarationSource
{
    /// <summary>
    ///     Gets the type of the metadata value this attribute declares, which is also its metadata key.
    /// </summary>
    /// <remarks>
    ///     Return the same type a definition would contribute, so that a definition for the same message overwrites
    ///     what the attribute declared.
    /// </remarks>
    Type DeclarationType { get; }

    /// <summary>
    ///     Creates the metadata value stored under <see cref="DeclarationType" />.
    /// </summary>
    /// <returns>The declared value. Must be assignable to <see cref="DeclarationType" /> and must not be null.</returns>
    object CreateDeclaration();
}
