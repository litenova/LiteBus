using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a handler that executes when an exception occurs during any command processing.
/// </summary>
/// <remarks>
///     Command error handlers provide centralized exception handling for the command pipeline.
///     They execute when any exception occurs during command processing (in pre-handlers, handlers, or post-handlers).
///     Multiple error handlers can be registered to implement different error handling strategies such as
///     logging, notifications, or custom recovery logic for all commands.
/// </remarks>
public interface ICommandErrorHandler : IRegistrableCommandConstruct, IAsyncMessageErrorHandler<ICommand, object>;