using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref cref="TCommand" /> pre-handle phase
/// </summary>
public interface ICommandPreHandler<in TCommand> : IRegistrableCommandConstruct, IAsyncMessagePreHandler<TCommand> where TCommand : ICommand
{
}