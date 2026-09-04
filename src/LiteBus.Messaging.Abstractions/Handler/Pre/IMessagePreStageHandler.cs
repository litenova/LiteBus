namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks a type as a handler in the pre stage so the message registry can discover it.
/// </summary>
/// <remarks>
///     <para>
///         This contract carries no members. The pipeline invokes a pre-stage handler through the closed contract
///         recorded in its descriptor at registration, rather than through a member on this interface, so that a class
///         implementing pipeline contracts for several message types still compiles.
///     </para>
///     <para>
///         Three roles carry this marker, and the contract each one implements decides which stage runs it:
///         <see cref="IMessageGuard{TMessage}" /> refuses a message, <see cref="IMessageShortcut{TMessage}" /> answers
///         one whose work is already done, and <see cref="IMessagePreHandler{TMessage}" /> prepares one that is going to
///         be handled. The framework runs them in that order.
///     </para>
///     <para>
///         The pre stage is the only stage whose family holds more than one role, which is why the family carries a name
///         of its own. The post and completion stages each hold a single role, so their marker and their role share a
///         name without ambiguity.
///     </para>
/// </remarks>
public interface IMessagePreStageHandler;
