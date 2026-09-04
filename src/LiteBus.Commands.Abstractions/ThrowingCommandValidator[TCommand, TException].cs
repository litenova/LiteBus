using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Adapts a command validator that signals failure by throwing to the <see cref="Validity" /> contract.
/// </summary>
/// <typeparam name="TCommand">The command type this validator runs for.</typeparam>
/// <typeparam name="TException">The exception type the validation body throws to report failure.</typeparam>
/// <remarks>
///     Migration scaffolding, meant to be deleted once every validator returns <see cref="Validity" /> directly. See
///     <see cref="ThrowingValidator{TMessage,TException}" /> for what it does and what it cannot recover. This
///     specialization exists because the command module registers command constructs only, and a validator implementing
///     the messaging contract alone is refused.
/// </remarks>
public abstract class ThrowingCommandValidator<TCommand, TException>
    : ThrowingValidator<TCommand, TException>, ICommandValidator<TCommand>
    where TCommand : ICommand
    where TException : Exception;
