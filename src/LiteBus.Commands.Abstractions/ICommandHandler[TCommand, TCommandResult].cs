using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a handler that processes a command of type <typeparamref name="TCommand"/> and returns a result of type <typeparamref name="TCommandResult"/>.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle.</typeparam>
/// <typeparam name="TCommandResult">The type of result to return after handling the command.</typeparam>
/// <remarks>
///     Command handlers are responsible for executing business logic in response to a specific command.
///     When implementing this interface, the handler should process the given command and return
///     the expected result. Each command of type <typeparamref name="TCommand"/> should have exactly
///     one handler within the application.
/// </remarks>
public interface ICommandHandler<in TCommand, TCommandResult> : IRegistrableCommandConstruct, IAsyncMessageHandler<TCommand, TCommandResult> where TCommand : ICommand<TCommandResult>;