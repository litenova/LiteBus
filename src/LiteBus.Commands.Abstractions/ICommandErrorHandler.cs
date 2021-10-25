using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions
{
    public interface ICommandErrorHandlerBase
    {
    }

    /// <summary>
    ///     Represents an action that is executed on each command error-handle phase
    /// </summary>
    public interface ICommandErrorHandler : ICommandErrorHandlerBase, IMessageErrorHandler<ICommandBase>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TCommand" /> error-handle phase
    /// </summary>
    public interface ICommandErrorHandler<in TCommand> : ICommandErrorHandlerBase, IMessageErrorHandler<TCommand>
        where TCommand : ICommandBase
    {
    }
}