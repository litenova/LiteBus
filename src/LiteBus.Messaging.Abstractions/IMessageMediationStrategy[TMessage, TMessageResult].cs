namespace LiteBus.Messaging.Abstractions;

public interface IMessageMediationStrategy<in TMessage, out TMessageResult> where TMessage : notnull
{
    TMessageResult Mediate(TMessage message, IMessageDependencies messageDependencies, IExecutionContext executionContext);
}