namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     The non-generic base of all message handlers
    /// </summary>
    public interface IMessageHandler
    {
        object Handle(IHandleContext context);
    }

    /// <summary>
    ///     The generic base of all message handlers
    /// </summary>
    public interface IMessageHandler<in TMessage, out TMessageResult> : IMessageHandler
    {
        object IMessageHandler.Handle(IHandleContext context)
        {
            return Handle(new HandleContext<TMessage>(context));
        }

        TMessageResult Handle(IHandleContext<TMessage> context);
    }
}