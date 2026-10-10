namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks a type as a post-handler so the message registry can discover it.
/// </summary>
/// <remarks>
///     This contract carries no members. The pipeline invokes a post-handler through the closed contract recorded in its
///     descriptor at registration, rather than through a member on this interface, so that a class implementing
///     post-handler contracts for several message types still compiles. Implement
///     <see cref="IMessagePostHandler{TMessage,TMessageResult}" />.
/// </remarks>
public interface IMessagePostHandler;
