using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One metadata value declared from composition code rather than from a definition class beside the message.
/// </summary>
/// <remarks>
///     <para>
///         A definition class is the right home for a declaration a message owns. This is for the other case: a value
///         that holds for a whole family of messages and would otherwise be copied into every one of them. Declaring
///         it against the marker interface the family already shares says it once.
///     </para>
///     <para>
///         The message type may be a concrete message, a base class, or a marker interface, and it covers every
///         message assignable to it. Resolution is unchanged: a declaration written closer to the message wins, so a
///         family default is overridden by a message that states its own position.
///     </para>
/// </remarks>
public sealed record MessageDeclarationItem
{
    /// <summary>
    ///     Gets the message type the declaration covers, and every message assignable to it.
    /// </summary>
    public required Type MessageType { get; init; }

    /// <summary>
    ///     Gets the metadata value type the declaration is keyed by.
    /// </summary>
    /// <value>
    ///     Usually the runtime type of <see cref="Value" />. Name a base type or interface to have readers look the
    ///     value up by that instead, which is the same freedom a definition class has.
    /// </value>
    public required Type DeclarationType { get; init; }

    /// <summary>
    ///     Gets the declared value.
    /// </summary>
    public required object Value { get; init; }

    /// <summary>
    ///     Creates a declaration for a message type and the value's own type.
    /// </summary>
    /// <typeparam name="TMessage">The message type, base type, or marker interface the declaration covers.</typeparam>
    /// <typeparam name="TValue">The metadata value type, which is also its key.</typeparam>
    /// <param name="value">The value to declare.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
    public static MessageDeclarationItem For<TMessage, TValue>(TValue value)
        where TMessage : notnull
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(value);

        return new MessageDeclarationItem
        {
            MessageType = typeof(TMessage),
            DeclarationType = typeof(TValue),
            Value = value
        };
    }
}
