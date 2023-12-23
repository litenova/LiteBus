using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteBus.Commands.Abstractions.Exceptions;

public sealed class MultipleCommandHandlerFoundException : Exception
{
    public MultipleCommandHandlerFoundException(Type commandType, IEnumerable<Type> handlerTypes)
        : base($"Multiple command handler found for command type {commandType.FullName}. " +
               $"Handler types: {string.Join(", ", handlerTypes.Select(x => x.FullName))}")
    {
    }
}