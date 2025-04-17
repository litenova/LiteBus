using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a handler that processes a command of type <typeparamref name="TCommand" /> without returning a result.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
/// <remarks>
///     Command handlers are responsible for executing business logic in response to a specific command.
///     When implementing this interface, the handler should process the given command and perform the
///     necessary state changes without returning data. Each command of type <typeparamref name="TCommand" />
///     should have exactly one handler within the application.
/// </remarks>
public interface ICommandHandler<in TCommand> : IRegistrableCommandConstruct, IAsyncMessageHandler<TCommand> where TCommand : ICommand;