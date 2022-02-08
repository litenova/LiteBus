using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents an action that is executed on each command post-handle phase
/// </summary>
public interface ICommandPostHandler : ICommandPostHandlerBase, IAsyncPostHandler<ICommandBase>
{
}