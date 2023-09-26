namespace LiteBus.Messaging.Abstractions;

public interface IMessageMediator
{
    TMessageResult Mediate<TMessage, TMessageResult>(TMessage message, MediateOptions<TMessage, TMessageResult> options) where TMessage : notnull;
}