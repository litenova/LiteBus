namespace Paykan.Abstractions
{
    /// <summary>
    /// The root of all message abstractions
    /// (e.g., <see cref="ICommand"/> and <see cref="IQuery{TQueryResult}"/>)
    /// </summary>
    public interface IMessage
    {
    }
    
    /// <summary>
    /// The root of all message abstractions
    /// (e.g., <see cref="ICommand"/> and <see cref="IQuery{TQueryResult}"/>)
    /// </summary>
    /// <typeparam name="TMessageResult">The message result type</typeparam>
    public interface IMessage<out TMessageResult> : IMessage
    {
    }
}