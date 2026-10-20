namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares the idempotency position of messages of type <typeparamref name="TMessage" />.
/// </summary>
/// <typeparam name="TMessage">The message type this definition describes.</typeparam>
/// <remarks>
///     The named specialization of <see cref="IMessageDefinition{TMessage,TValue}" /> for
///     <see cref="IdempotencyDeclaration" />, so a definition class reads as what it means rather than as a generic
///     pair of type arguments.
/// </remarks>
public interface IIdempotencyDefinition<TMessage> : IMessageDefinition<TMessage, IdempotencyDeclaration>
    where TMessage : notnull
{
    /// <summary>
    ///     Gets the idempotency declaration for the message.
    /// </summary>
    IdempotencyDeclaration Idempotency { get; }

    /// <inheritdoc />
    IdempotencyDeclaration IMessageDefinition<TMessage, IdempotencyDeclaration>.Value => Idempotency;
}
