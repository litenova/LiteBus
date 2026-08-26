namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks a type as a pre-handler so the message registry can discover it.
/// </summary>
/// <remarks>
///     <para>
///         This contract carries no members. The pipeline invokes a pre-handler through the closed contract recorded in
///         its descriptor at registration, rather than through a member on this interface, so that a class implementing
///         pre-handler contracts for several message types still compiles.
///     </para>
///     <para>
///         Implement <see cref="IMessagePreHandler{TMessage}" /> to validate, authorize, or enrich, or a gate,
///         <see cref="IMessageGate{TMessage}" /> or <see cref="IMessageGate{TMessage,TMessageResult}" />, to decide
///         whether the message reaches its main handler at all.
///     </para>
/// </remarks>
public interface IMessagePreHandler;
