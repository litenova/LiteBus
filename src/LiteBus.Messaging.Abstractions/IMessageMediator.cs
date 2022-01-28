namespace LiteBus.Messaging.Abstractions;

public interface IMessageMediator
{
    TMessageResult Mediate<TMessage, TMessageResult>(TMessage message,
                                                     IDiscoveryWorkflow discovery,
                                                     IExecutionWorkflow<TMessage, TMessageResult> execution);
}