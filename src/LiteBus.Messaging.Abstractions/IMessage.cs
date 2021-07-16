namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     Represents a message with result
    /// </summary>
    /// <typeparam name="TMessageResult">The message result type</typeparam>
    public interface IMessage<out TMessageResult> : IMessage
    {
    }

    /// <summary>
    ///     Represents a message without result and acts as the base of all messages
    /// </summary>
    public interface IMessage
    {
    }
}