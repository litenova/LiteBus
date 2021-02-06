using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Commands.Abstraction
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