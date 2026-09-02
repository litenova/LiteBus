using System;
using System.Diagnostics.CodeAnalysis;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Reads the declarative metadata of a message type from application code.
/// </summary>
/// <remarks>
///     <para>
///         Resolve it from the container wherever a cross-cutting handler has to read what a message declared. It is the
///         supported way in: the alternative is to inject <see cref="IMessageRegistry" />, call
///         <see cref="IMessageReader.Find" />, and reach through <see cref="IMessageDescriptor.Metadata" />, which makes
///         the registry's descriptor shape part of every application that wants to read its own declarations.
///     </para>
///     <para>
///         This is what a generic guard needs. A message declares the permission it requires through an
///         <c>IMessageDefinition&lt;TMessage, RequiredPermission&gt;</c> or an attribute; one guard reads the
///         declaration back and enforces it for every message that has one, instead of one guard per message.
///     </para>
///     <para>
///         Nothing here is specific to auditing, and nothing here needs auditing to be enabled. LiteBus's own audit
///         declarations are readable through the same surface as an application's, because they go into the same
///         type-keyed bag.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class PermissionGuard<TMessage> : IMessageGuard<TMessage>
///     where TMessage : notnull
/// {
///     private readonly IMessageMetadataAccessor _metadata;
///     private readonly ICurrentActor _actor;
///
///     public PermissionGuard(IMessageMetadataAccessor metadata, ICurrentActor actor)
///     {
///         _metadata = metadata;
///         _actor = actor;
///     }
///
///     public Task<Verdict> DecideAsync(TMessage message, CancellationToken cancellationToken = default)
///     {
///         if (!_metadata.TryGet<RequiredPermission>(typeof(TMessage), out var required))
///         {
///             return Task.FromResult(Verdict.Allow);
///         }
///
///         return Task.FromResult(_actor.Holds(required)
///             ? Verdict.Allow
///             : Verdict.Deny($"the caller does not hold {required.Name}"));
///     }
/// }
/// ]]></code>
/// </example>
public interface IMessageMetadataAccessor
{
    /// <summary>
    ///     Gets the metadata declared for a message type.
    /// </summary>
    /// <param name="messageType">The message type to read. A closed generic message resolves to its generic type definition.</param>
    /// <returns>The metadata declared for that message type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messageType" /> is <see langword="null" />.</exception>
    /// <exception cref="MessageMetadataNotFoundException">The type is not registered as a message.</exception>
    /// <remarks>
    ///     An unregistered type is reported rather than answered with an empty collection. A handler asking about a
    ///     message that never reached the registry is looking at a registration bug, and an empty answer would hide it
    ///     behind a permission check that silently passes.
    /// </remarks>
    IMessageMetadata ForMessage(Type messageType);

    /// <summary>
    ///     Gets the metadata declared for a message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to read.</typeparam>
    /// <returns>The metadata declared for that message type.</returns>
    /// <exception cref="MessageMetadataNotFoundException">The type is not registered as a message.</exception>
    IMessageMetadata ForMessage<TMessage>()
        where TMessage : notnull;

    /// <summary>
    ///     Attempts to read one declared value for a message type.
    /// </summary>
    /// <typeparam name="TValue">The metadata value type to look up, which is also its key.</typeparam>
    /// <param name="messageType">The message type to read.</param>
    /// <param name="value">When this method returns <see langword="true" />, the declared value.</param>
    /// <returns>
    ///     <see langword="true" /> when the message declares a value of that type; otherwise, <see langword="false" />.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="messageType" /> is <see langword="null" />.</exception>
    /// <exception cref="MessageMetadataNotFoundException">The type is not registered as a message.</exception>
    /// <remarks>
    ///     This is the shape a cross-cutting handler wants: one call that answers both whether the declaration is
    ///     present and what it says.
    /// </remarks>
    bool TryGet<TValue>(Type messageType, [MaybeNullWhen(false)] out TValue value)
        where TValue : notnull;

    /// <summary>
    ///     Attempts to read one declared value for a message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to read.</typeparam>
    /// <typeparam name="TValue">The metadata value type to look up, which is also its key.</typeparam>
    /// <param name="value">When this method returns <see langword="true" />, the declared value.</param>
    /// <returns>
    ///     <see langword="true" /> when the message declares a value of that type; otherwise, <see langword="false" />.
    /// </returns>
    /// <exception cref="MessageMetadataNotFoundException">The type is not registered as a message.</exception>
    bool TryGet<TMessage, TValue>([MaybeNullWhen(false)] out TValue value)
        where TMessage : notnull
        where TValue : notnull;
}
