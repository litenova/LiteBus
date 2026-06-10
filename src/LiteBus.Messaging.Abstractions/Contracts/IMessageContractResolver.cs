using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Resolves the CLR type used for contract lookup when accepting or enqueuing a message.
/// </summary>
/// <remarks>
///     <para>
///         When no resolver is registered, inbox and outbox writers use <paramref name="message" />.GetType() for
///         contract lookup. Register a resolver when the declared parameter type should drive contract selection instead
///         of the runtime instance type.
///     </para>
///     <para>
///         On-demand <see cref="MessageContractAttribute" /> resolution in <see cref="IContractReader.GetContract" />
///         remains available regardless of resolver registration.
///     </para>
/// </remarks>
public interface IMessageContractResolver
{
    /// <summary>
    ///     Returns the CLR type passed to <see cref="IContractReader.GetContract" /> for the supplied message.
    /// </summary>
    /// <param name="declaredType">The declared message type supplied by the caller.</param>
    /// <param name="message">The message instance being accepted or enqueued.</param>
    /// <returns>The CLR type used for contract lookup.</returns>
    Type ResolveContractType(Type declaredType, object message);
}
