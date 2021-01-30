using System.Windows.Input;

namespace Paykan.Messaging.Abstractions
{
    /// <summary>
    /// The root of all message abstractions
    /// (e.g., <see cref="ICommand"/> and <see cref="Paykan.Abstractions.IQuery{TQueryResult}"/>)
    /// </summary>
    public interface IMessage
    {
    }
    
    /// <summary>
    /// The root of all message abstractions
    /// (e.g., <see cref="ICommand"/> and <see cref="Paykan.Abstractions.IQuery{TQueryResult}"/>)
    /// </summary>
    /// <typeparam name="TMessageResult">The message result type</typeparam>
    public interface IMessage<out TMessageResult> : IMessage
    {
    }
}