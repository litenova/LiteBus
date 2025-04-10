using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a post-handler that executes after a specific command type <typeparamref name="TCommand"/> is processed.
/// </summary>
/// <typeparam name="TCommand">The specific command type this post-handler targets.</typeparam>
/// <remarks>
///     Type-specific command post-handlers run after the main command handler for the specified command type.
///     They can be used for command-specific logging, notification, or other cross-cutting concerns that apply
///     only to the specified command type. Multiple type-specific post-handlers can be registered for each command type.
/// </remarks>
public interface ICommandPostHandler<in TCommand> : IRegistrableCommandConstruct, IAsyncMessagePostHandler<TCommand> where TCommand : ICommand;