using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

/// <summary>
///     Describes an error handler registered for a message type.
/// </summary>
internal sealed class ErrorHandlerDescriptor : HandlerDescriptorBase, IErrorHandlerDescriptor
{
    /// <summary>
    ///     Gets the CLR type of the message result handled by the error handler.
    /// </summary>
    public required Type MessageResultType { get; init; }
}
