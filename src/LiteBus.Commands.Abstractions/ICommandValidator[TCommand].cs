using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Decides whether a command of type <typeparamref name="TCommand" /> is well-formed.
/// </summary>
/// <typeparam name="TCommand">The specific command type this validator runs for.</typeparam>
/// <remarks>
///     A validator returns <see cref="Validity" /> rather than throwing, so a malformed command reports
///     <see cref="MessageOutcome.Invalid" /> instead of arriving at error handlers as a fault. Every validator for the
///     command runs and their failures are collected, so the caller sees all of them at once.
/// </remarks>
public interface ICommandValidator<in TCommand> : IMessageValidator<TCommand>
    where TCommand : ICommand;
