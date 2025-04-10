using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteBus.Commands.Abstractions.Exceptions;

/// <summary>
/// Represents an exception that is thrown when multiple command handlers are found for a command type
/// that should have exactly one handler.
/// </summary>
/// <remarks>
/// In the CQRS pattern, each command should be handled by exactly one handler to ensure
/// clear responsibility and predictable behavior. This exception is thrown when the system
/// detects that multiple handlers have been registered for the same command type, which
/// violates this principle.
/// 
/// The exception includes information about the command type and the types of all handlers
/// found, which helps in diagnosing and resolving the issue.
/// </remarks>
public sealed class MultipleCommandHandlerFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MultipleCommandHandlerFoundException"/> class with
    /// information about the command type and the handler types found.
    /// </summary>
    /// <param name="commandType">The type of the command for which multiple handlers were found.</param>
    /// <param name="handlerTypes">A collection of the types of all handlers found for the command.</param>
    /// <remarks>
    /// The exception message includes the full name of the command type and a comma-separated list
    /// of the full names of all handler types found, providing detailed information for debugging.
    /// </remarks>
    public MultipleCommandHandlerFoundException(Type commandType, IEnumerable<Type> handlerTypes)
        : base($"Multiple command handler found for command type {commandType.FullName}. " +
               $"Handler types: {string.Join(", ", handlerTypes.Select(x => x.FullName))}")
    {
    }
}