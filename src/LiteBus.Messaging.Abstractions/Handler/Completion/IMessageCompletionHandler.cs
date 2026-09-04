namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks a type as a completion handler so the message registry can discover it.
/// </summary>
/// <remarks>
///     <para>
///         This contract carries no members. The pipeline invokes a completion handler through the closed contract
///         recorded in its descriptor at registration, rather than through a member on this interface, so that a class
///         observing several message types still compiles.
///     </para>
///     <para>
///         Completion handlers close the gap left by post-handlers and error handlers. Post-handlers run only when the
///         main handler succeeds, and error handlers run only for recoverable exceptions. A completion handler runs on
///         every path, exactly once, which makes it the only stage that can record how a message actually ended.
///     </para>
///     <para>
///         A completion handler observes; it cannot change the outcome. Implement
///         <see cref="IMessageCompletionHandler{TMessage}" />, or
///         <see cref="IMessageCompletionHandler{TMessage,TMessageResult}" /> when the handler needs the result typed.
///     </para>
/// </remarks>
public interface IMessageCompletionHandler;
