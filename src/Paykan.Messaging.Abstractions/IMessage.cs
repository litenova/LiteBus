namespace Paykan.Messaging.Abstractions
{
    /// <summary>
    ///     The base of all messages
    /// </summary>
    public interface IMessage
    {
    }

    /// <summary>
    ///     The base of all messages with result
    /// </summary>
    /// <typeparam name="TMessageResult">The message result type</typeparam>
    public interface IMessage<out TMessageResult> : IMessage
    {
    }
}