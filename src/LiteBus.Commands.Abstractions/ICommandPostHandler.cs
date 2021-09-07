using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions
{
    public interface ICommandPostHandlerBase
    {
    }

    /// <summary>
    ///     Represents an action that is executed on each command post-handle phase
    /// </summary>
    public interface ICommandPostHandler : ICommandPostHandlerBase, IMessagePostHandler<ICommandBase>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TCommand" /> post-handle phase
    /// </summary>
    public interface ICommandPostHandler<in TCommand> : ICommandPostHandlerBase, IMessagePostHandler<TCommand>
        where TCommand : ICommandBase
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TCommand" /> post-handle phase
    /// </summary>
    public interface ICommandPostHandler<in TCommand, in TCommandResult> : ICommandPostHandlerBase,
                                                                           IMessagePostHandler<TCommand, TCommandResult>
        where TCommand : ICommand<TCommandResult>
    {
    }
}