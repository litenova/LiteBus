namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks a type as a refusal mapper so the message registry can discover it.
/// </summary>
/// <remarks>
///     This contract carries no members. The pipeline invokes a mapper through the closed contract recorded in its
///     descriptor at registration, rather than through a member on this interface, so that a class mapping refusals for
///     several message types still compiles. Implement
///     <see cref="IMessageRefusalMapper{TMessage,TMessageResult}" />.
/// </remarks>
public interface IMessageRefusalMapper;
