namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageMediator
    {
        TMessageResult Mediate<TMessage, TMessageResult>(TMessage message,
                                                         IMessageResolveStrategy messageResolveStrategy,
                                                         IMessageMediationStrategy<TMessage, TMessageResult> messageMediationStrategy);
    }
}