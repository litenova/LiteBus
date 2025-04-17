namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a descriptor for an error handler, providing metadata about the handler such as the message type it
///     handles,
///     its execution order, and any associated tags.
/// </summary>
public interface IErrorHandlerDescriptor : IHandlerDescriptor;