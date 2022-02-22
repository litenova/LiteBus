using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Workflows.Extensions;

namespace LiteBus.Messaging.Workflows.Execution.PreHandle;

public class SyncSequentialPreHandleExecution<TMessage> : IPreHandleExecutionWorkflow<TMessage, NoResult>
{
    public NoResult Execute(TMessage message,
                            IResolutionContext resolutionContext,
                            IHandleContext<TMessage> handleContext)
    {
        foreach (var syncPreHandler in resolutionContext.IndirectPreHandlers.GetSyncHandlers())
        {
            syncPreHandler.Handle(handleContext);
        }

        foreach (var syncPreHandler in resolutionContext.PreHandlers.GetSyncHandlers())
        {
            syncPreHandler.Handle(handleContext);
        }

        return new NoResult();
    }
}