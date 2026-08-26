namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a descriptor for a completion handler, providing metadata about the handler such as the message type
///     it observes, its execution order, and any associated tags.
/// </summary>
public interface ICompletionHandlerDescriptor : IHandlerDescriptor;
