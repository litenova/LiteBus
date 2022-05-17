using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref cref="TCommand" /> error-handle phase
/// </summary>
public interface ICommandErrorHandler<in TCommand> : ICommandErrorHandlerBase, IMessageErrorHandler<TCommand>
    where TCommand : ICommandBase
{
}