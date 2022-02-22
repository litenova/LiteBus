namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the pre-handle execution workflow of a given message
/// </summary>
/// <typeparam name="TMessage">Type of message</typeparam>
/// <typeparam name="TOutput">Type of output of execution workflow. It also indicates the mode of workflow (e.g., Sync and Async)</typeparam>
public interface IPreHandleExecutionWorkflow<in TMessage, out TOutput> where TMessage : notnull
{
    TOutput Execute(TMessage message, IResolutionContext resolutionContext, IHandleContext<TMessage> handleContext);
}