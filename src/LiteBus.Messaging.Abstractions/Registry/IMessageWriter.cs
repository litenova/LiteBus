using System;
using System.Diagnostics.CodeAnalysis;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Write-only surface for message and handler type registration.
///     Consumed by module builders at configuration time.
/// </summary>
/// <remarks>
///     Accepts handler types, message types, and open generic handler types.
///     The registry determines the appropriate descriptor building strategy for each registered type.
/// </remarks>
public interface IMessageWriter
{
    /// <summary>
    ///     Registers a type with the message system.
    /// </summary>
    /// <param name="type">
    ///     The type to register. When the type implements a handler interface, handler descriptors are built
    ///     and linked to relevant message descriptors. When the type is a plain message type, a message descriptor
    ///     is created and linked to handlers already registered.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type" /> is <see langword="null" />.</exception>
    [RequiresUnreferencedCode("Handler and message registration inspects CLR types via reflection.")]
    void Register(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.PublicMethods
            | DynamicallyAccessedMemberTypes.Interfaces)]
        Type type);

    /// <summary>
    ///     Registers a type discovered by scanning an assembly rather than named by the composition code.
    /// </summary>
    /// <param name="type">The type to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         Behaves exactly like <see cref="Register" /> and additionally records that nothing in the composition
    ///         code named this type. That distinction only matters for an open generic pipeline handler, which inserts
    ///         a stage into every message it fits: <c>RequireExplicitOpenGenerics</c> uses it to fail composition for
    ///         one that arrived through a scan, and the composition summary reports the closure count either way.
    ///     </para>
    ///     <para>
    ///         The default implementation delegates to <see cref="Register" />, so a custom writer or a test double
    ///         compiles unchanged and simply cannot tell the two apart.
    ///     </para>
    /// </remarks>
    [RequiresUnreferencedCode("Handler and message registration inspects CLR types via reflection.")]
    void RegisterFromScan(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.PublicMethods
            | DynamicallyAccessedMemberTypes.Interfaces)]
        Type type)
    {
        Register(type);
    }

    /// <summary>
    ///     Declares one metadata value for a message type, without a definition class.
    /// </summary>
    /// <param name="declaration">The message type the value covers, the type it is keyed by, and the value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="declaration" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         Applied exactly as a definition's declaration is, including the rule that a declaration written closer
    ///         to the message wins. That is what makes a value declared against a marker interface a default rather
    ///         than an override: the family gets it, and a message stating its own position keeps it.
    ///     </para>
    ///     <para>
    ///         The default implementation throws. A writer that cannot record declarations must say so rather than
    ///         accept the call and drop it, because a silently dropped authorization default is an unguarded command
    ///         that looks configured.
    ///     </para>
    /// </remarks>
    void AddDeclaration(MessageDeclarationItem declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        throw new NotSupportedException(
            $"This {nameof(IMessageWriter)} implementation does not record declarations, so the declaration of "
            + $"'{declaration.DeclarationType.Name}' for '{declaration.MessageType.Name}' would be dropped. "
            + "Implement AddDeclaration, or declare the value with a definition class beside the message.");
    }
}