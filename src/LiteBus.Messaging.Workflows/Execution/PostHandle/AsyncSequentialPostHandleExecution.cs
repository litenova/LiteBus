using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Workflows.Extensions;

namespace LiteBus.Messaging.Workflows.Execution.PostHandle;

public class AsyncSequentialPostHandleExecution<TMessage, TMessageResult> :
    IPostHandleExecutionWorkflow<TMessage, TMessageResult, Task>
{
    public async Task Execute(TMessage message,
                              IResolutionContext resolutionContext,
                              IHandleContext<TMessage, TMessageResult> handleContext)
    {
        foreach (var asyncPostHandler in resolutionContext.PostHandlers.GetAsyncHandlers())
        {
            await (Task) asyncPostHandler.Handle(handleContext);
        }

        foreach (var asyncPostHandler in resolutionContext.IndirectPostHandlers.GetAsyncHandlers())
        {
            await (Task) asyncPostHandler.Handle(handleContext);
        }
    }
}