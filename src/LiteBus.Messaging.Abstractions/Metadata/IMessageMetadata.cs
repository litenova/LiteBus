using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Exposes the declarative metadata associated with one message type.
/// </summary>
/// <remarks>
///     <para>
///         Metadata is resolved once, when the message type is registered, and is keyed by the CLR type of each value.
///         Pipeline stages outside the handler read it to decide behavior without reflecting over the message on every
///         dispatch.
///     </para>
///     <para>
///         Two sources populate this collection, and both contribute values of the same type. Attributes that implement
///         <see cref="IMessageDeclarationSource" /> are applied first, then message definitions, so an explicit
///         definition always overwrites an attribute declaring the same value type. Attributes that do not implement
///         that interface are not metadata and are never collected, which keeps the collection bounded and predictable.
///     </para>
/// </remarks>
public interface IMessageMetadata
{
    /// <summary>
    ///     Gets every metadata value associated with the message type.
    /// </summary>
    IReadOnlyCollection<object> Values { get; }

    /// <summary>
    ///     Attempts to get the metadata value stored under <typeparamref name="TValue" />.
    /// </summary>
    /// <typeparam name="TValue">The metadata value type to look up.</typeparam>
    /// <param name="value">When this method returns <see langword="true" />, the stored value.</param>
    /// <returns><see langword="true" /> when a value of that type is present; otherwise, <see langword="false" />.</returns>
    bool TryGet<TValue>([MaybeNullWhen(false)] out TValue value)
        where TValue : notnull;

    /// <summary>
    ///     Determines whether a metadata value of type <typeparamref name="TValue" /> is present.
    /// </summary>
    /// <typeparam name="TValue">The metadata value type to look for.</typeparam>
    /// <returns><see langword="true" /> when a value of that type is present; otherwise, <see langword="false" />.</returns>
    bool Contains<TValue>()
        where TValue : notnull;
}
