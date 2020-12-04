using System.Threading.Tasks;

namespace Paykan.Abstractions
{
    /// <summary>
    /// Represents a command without result that is intended to change the application state
    /// </summary>
    public interface ICommand : IMessage<Task>
    {
        
    }
    
    /// <summary>
    /// Represents a command with result that is intended to change the application state
    /// </summary>
    public interface ICommand<TCommandResult> : IMessage<Task<TCommandResult>>
    {
        
    }
}