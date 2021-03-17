using System.Threading.Tasks;
using LightBus.Messaging.Abstractions;

namespace LightBus.Commands.Abstractions
{
    /// <summary>
    ///     Represents a command without result
    /// </summary>
    public interface ICommand : IBaseCommand, IMessage<Task>
    {
    }

    /// <summary>
    ///     Represents a command with result
    /// </summary>
    public interface ICommand<TCommandResult> : IBaseCommand, IMessage<Task<TCommandResult>>
    {
    }
}