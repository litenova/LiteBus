using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Abstractions
{
    /// <summary>
    ///     Represents a command without result
    /// </summary>
    public interface ICommand
    {
    }

    /// <summary>
    ///     Represents a command with result
    /// </summary>
    public interface ICommand<TCommandResult> : ICommand
    {
        
    }
}