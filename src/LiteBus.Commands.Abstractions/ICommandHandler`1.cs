using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents the definition of a handler that handles a command without result
/// </summary>
/// <typeparam name="TCommand">The type of command</typeparam>
public interface ICommandHandler<in TCommand> : ICommandHandlerBase, IAsyncHandler<TCommand>
    where TCommand : ICommand
{
}