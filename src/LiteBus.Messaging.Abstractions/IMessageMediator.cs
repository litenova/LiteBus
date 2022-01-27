namespace LiteBus.Messaging.Abstractions;

public interface IMessageMediator
{
    TMessageResult Mediate<TMessage, TMessageResult>(TMessage message,
                                                     IDiscoveryWorkflow discoveryWorkflow,
                                                     IExecutionWorkflow<TMessage, TMessageResult>
                                                         executionWorkflow);
}