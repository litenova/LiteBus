using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Turns a refused command into the result the caller expects.
/// </summary>
/// <typeparam name="TCommand">The command type this mapper covers.</typeparam>
/// <typeparam name="TCommandResult">The type of result the command produces.</typeparam>
/// <remarks>
///     Register this against <see cref="ICommand" /> to cover every command that produces
///     <typeparamref name="TCommandResult" />, or against a concrete command to override that for one message. Without a
///     mapper, a refusal reaches the caller as <see cref="LiteBusMessageDeniedException" /> or
///     <see cref="LiteBusMessageInvalidException" />.
/// </remarks>
public interface ICommandRefusalMapper<in TCommand, out TCommandResult>
    : IMessageRefusalMapper<TCommand, TCommandResult>
    where TCommand : ICommand;
