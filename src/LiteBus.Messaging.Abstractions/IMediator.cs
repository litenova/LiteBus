namespace LiteBus.Messaging.Abstractions;

public interface IMediator
{
    TMessageResult Mediate<TMessage, TMessageResult>(TMessage message,
                                                     IDiscoveryWorkflow discovery,
                                                     IResolutionWorkflow resolution,
                                                     IExecutionWorkflow<TMessage, TMessageResult> execution);
}