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
///         Three kinds of handler carry this marker, and the contract each one implements decides which stage runs it.
///         <see cref="IMessageGuard{TMessage}" /> refuses a message, <see cref="IMessageShortcut{TMessage}" /> answers
///         one whose work is already done, and <see cref="IMessagePreHandler{TMessage}" /> validates or enriches a
///         message that is going to be handled. The framework runs them in that order.
///     </para>
/// </remarks>
public interface IMessagePreHandler;
