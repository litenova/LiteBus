namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageFindStrategy<TMessage>
    {
        IMessageDescriptor Find(IMessageRegistry messageRegistry);
    }
}