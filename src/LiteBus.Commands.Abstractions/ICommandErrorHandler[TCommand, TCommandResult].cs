using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a handler that executes when an exception occurs during the processing of a specific
///     command type <typeparamref name="TCommand" /> that returns <typeparamref name="TCommandResult" />.
/// </summary>
/// <typeparam name="TCommand">The specific command type this error handler targets.</typeparam>
/// <typeparam name="TCommandResult">The result type produced by the command handler.</typeparam>
/// <remarks>
///     Typed command error handlers can set <see cref="MessageErrorContext{TCommand,TCommandResult}.Outcome" /> and
///     <see cref="MessageErrorContext{TCommand,TCommandResult}.HandledResult" /> to suppress recoverable exceptions and
///     return a fallback result.
/// </remarks>
public interface ICommandErrorHandler<TCommand, TCommandResult>
    : IAsyncMessageErrorHandler<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>;
