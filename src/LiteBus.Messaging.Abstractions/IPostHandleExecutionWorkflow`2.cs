namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the post-handle execution workflow of a given message
/// </summary>
/// <typeparam name="TMessage">Type of message</typeparam>
/// <typeparam name="TOutput">Type of output of execution workflow. It also indicates the mode of workflow (e.g., Sync and Async)</typeparam>
/// <typeparam name="TMessageResult">Type of message result</typeparam>
public interface IPostHandleExecutionWorkflow<in TMessage, in TMessageResult, out TOutput> where TMessage : notnull
{
    TOutput Execute(TMessage message,
                    IResolutionContext resolutionContext,
                    IHandleContext<TMessage, TMessageResult> handleContext);
}