using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents the definition of a sync handler that handles a command without result
/// </summary>
/// <typeparam name="TCommand">The type of command</typeparam>
public interface ISyncCommandHandler<in TCommand> : ICommandHandler, ISyncHandler<TCommand> where TCommand : ICommand
{
}