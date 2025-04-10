using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a post-handler that executes after any command is processed.
/// </summary>
/// <remarks>
///     Command post-handlers run after the main command handler has completed execution. They can be used for
///     cross-cutting concerns such as logging, notification, or cleanup operations that should run after all commands.
///     Multiple post-handlers can be registered in the application and they will all execute after each command is handled.
/// </remarks>
public interface ICommandPostHandler : IRegistrableCommandConstruct, IAsyncMessagePostHandler<ICommand>;