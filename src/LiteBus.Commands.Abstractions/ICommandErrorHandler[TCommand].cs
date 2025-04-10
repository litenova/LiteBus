using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a handler that executes when an exception occurs during the processing of a specific
///     command type <typeparamref name="TCommand"/>.
/// </summary>
/// <typeparam name="TCommand">The specific command type this error handler targets.</typeparam>
/// <remarks>
///     Command type-specific error handlers provide targeted exception handling for particular command types.
///     They execute when an exception occurs during the processing of the specified command type.
///     This allows for implementing specialized error handling strategies for different command types,
///     such as custom recovery logic or specific error reporting for critical commands.
/// </remarks>
public interface ICommandErrorHandler<in TCommand> : IRegistrableCommandConstruct, IAsyncMessageErrorHandler<TCommand, object> where TCommand : ICommand;