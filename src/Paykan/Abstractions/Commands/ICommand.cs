using System.Threading.Tasks;
using Paykan.Messaging.Abstractions;

namespace Paykan.Abstractions
{
    /// <summary>
    /// The root of all commands 
    /// </summary>
    public interface IBaseCommand : IMessage
    {
        
    }
    
    /// <summary>
    /// Represents a command without result that is intended to change the application state
    /// </summary>
    public interface ICommand : IBaseCommand, IMessage<Task>
    {
        
    }
    
    /// <summary>
    /// Represents a command with result that is intended to change the application state
    /// </summary>
    public interface ICommand<TCommandResult> : IBaseCommand, IMessage<Task<TCommandResult>>
    {
        
    }
}