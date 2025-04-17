namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents a command that produces a result of type <typeparamref name="TCommandResult" /> when handled.
/// </summary>
/// <typeparam name="TCommandResult">The type of result that will be returned when the command is processed.</typeparam>
/// <remarks>
///     This interface extends the base <see cref="ICommand" /> interface to support commands that need to return
///     data to the caller. While regular commands are used for state-changing operations without returning data,
///     commands with results allow for obtaining computed values or status information from the handler.
/// </remarks>
public interface ICommand<TCommandResult> : ICommand;