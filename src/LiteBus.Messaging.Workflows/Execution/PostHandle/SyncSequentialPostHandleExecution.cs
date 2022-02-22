using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Workflows.Extensions;

namespace LiteBus.Messaging.Workflows.Execution.PostHandle;

public class SyncSequentialPostHandleExecution<TMessage, TMessageResult> :
    IPostHandleExecutionWorkflow<TMessage, TMessageResult, NoResult>
{
    public NoResult Execute(TMessage message,
                            IResolutionContext resolutionContext,
                            IHandleContext<TMessage, TMessageResult> handleContext)
    {
        foreach (var syncPostHandler in resolutionContext.PostHandlers.GetSyncHandlers())
        {
            syncPostHandler.Handle(handleContext);
        }

        foreach (var syncPostHandler in resolutionContext.IndirectPostHandlers.GetSyncHandlers())
        {
            syncPostHandler.Handle(handleContext);
        }

        return new NoResult();
    }
}