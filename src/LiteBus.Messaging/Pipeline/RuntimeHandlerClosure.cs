using System;

namespace LiteBus.Messaging;

/// <summary>
///     A handler registered for a generic message, closed over one runtime message type.
/// </summary>
/// <param name="HandlerType">The closed handler type to resolve from the service provider.</param>
/// <param name="ContractType">The closed contract the pipeline invokes the handler through.</param>
internal sealed record RuntimeHandlerClosure(Type HandlerType, Type ContractType);
