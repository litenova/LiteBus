using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents an action that is executed on each command pre-handle phase
/// </summary>
public interface ICommandPreHandler : IRegistrableCommandConstruct, IAsyncMessagePreHandler<ICommand>
{
}