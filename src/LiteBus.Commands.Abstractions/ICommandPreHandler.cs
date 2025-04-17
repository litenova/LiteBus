using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a pre-handler that executes before any command is processed.
/// </summary>
/// <remarks>
///     Command pre-handlers run before the main command handler is executed. They can be used for
///     cross-cutting concerns such as logging, validation, or security checks that should be applied to all commands.
///     Multiple pre-handlers can be registered in the application and they will all execute before each command is
///     handled.
/// </remarks>
public interface ICommandPreHandler : IRegistrableCommandConstruct, IAsyncMessagePreHandler<ICommand>;