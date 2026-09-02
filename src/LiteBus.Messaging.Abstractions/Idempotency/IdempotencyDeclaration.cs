using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that a message must not be applied twice, and how to recognise a repeat.
/// </summary>
/// <remarks>
///     <para>
///         The key is what makes a repeat recognisable. It has to come from the message rather than from the clock or a
///         counter, because two deliveries of the same intent are the same message and nothing else about them is
///         stable.
///     </para>
///     <para>
///         The selector runs once per mediation, so keep it a pure projection: read fields off the message and format
///         them. It cannot resolve services, because a declaration is created once at registration and takes no
///         dependencies. Anything needing a lookup belongs in a guard, which can hand its result forward through
///         <see cref="IExecutionContext.Data" />.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class ApplyPaymentCommandDefinition : IIdempotencyDefinition<ApplyPaymentCommand>
/// {
///     public IdempotencyDeclaration Idempotency =>
///         IdempotencyDeclaration.KeyedBy<ApplyPaymentCommand>(command => command.PaymentId.ToString())
///             with { Scope = "payments" };
/// }
/// ]]></code>
/// </example>
public sealed record IdempotencyDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyDeclaration" /> class.
    /// </summary>
    /// <param name="keySelector">Projects the idempotency key from the message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector" /> is <see langword="null" />.</exception>
    public IdempotencyDeclaration(Func<object, string> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        KeySelector = keySelector;
    }

    /// <summary>
    ///     Gets the projection from the message to its idempotency key.
    /// </summary>
    public Func<object, string> KeySelector { get; init; }

    /// <summary>
    ///     Gets the prefix that separates this message's keys from every other message's.
    /// </summary>
    /// <value>
    ///     The scope, or <see langword="null" /> to use the message type's name. Set it when two message types share
    ///     one key space on purpose, such as a command and the event that replays it.
    /// </value>
    public string? Scope { get; init; }

    /// <summary>
    ///     Gets a value indicating whether a repeat replays the result the first attempt produced.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> to replay the stored result, which requires the result to be serializable.
    ///     <see langword="false" /> (the default) to answer a repeat without a value, which is all a message producing
    ///     no result needs.
    /// </value>
    /// <remarks>
    ///     Replaying costs a serialized copy of every result in the store, and makes the store hold data the message
    ///     produced rather than only the fact that it ran. Turn it on for a message whose caller needs the same answer
    ///     twice, such as an endpoint a client retries; leave it off for a redelivered message nobody is waiting on.
    /// </remarks>
    public bool ReplayResult { get; init; }

    /// <summary>
    ///     Creates a declaration whose key is projected from a message of a known type.
    /// </summary>
    /// <typeparam name="TMessage">The message type the selector reads.</typeparam>
    /// <param name="keySelector">Projects the idempotency key from the message.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The cast inside is safe because a declaration only ever reaches messages assignable to the type it was
    ///     declared for.
    /// </remarks>
    public static IdempotencyDeclaration KeyedBy<TMessage>(Func<TMessage, string> keySelector)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new IdempotencyDeclaration(message => keySelector((TMessage) message));
    }
}
