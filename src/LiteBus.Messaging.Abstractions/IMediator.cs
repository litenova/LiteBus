namespace LiteBus.Messaging.Abstractions;

public interface IMediator
{
    TMessageResult Mediate<TMessage, TMessageResult>(TMessage message,
                                                     IDiscoveryWorkflow discovery,
                                                     IExecutionWorkflow<TMessage, TMessageResult> execution);
}