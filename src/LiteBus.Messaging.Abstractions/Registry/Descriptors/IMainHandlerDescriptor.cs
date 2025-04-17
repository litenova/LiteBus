using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a descriptor for a main handler, providing metadata about the handler such as the message type it
///     handles,
///     its execution order, any associated tags, and the type of the result produced by the handler.
/// </summary>
public interface IMainHandlerDescriptor : IHandlerDescriptor
{
    /// <summary>
    ///     Gets the type of the result produced by the main handler.
    /// </summary>
    Type MessageResultType { get; }
}