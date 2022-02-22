using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Workflows.Extensions;

namespace LiteBus.Messaging.Workflows.Execution.PreHandle;

public class AsyncSequentialPreHandleExecution<TMessage> : IPreHandleExecutionWorkflow<TMessage, Task>
{
    public async Task Execute(TMessage message,
                              IResolutionContext resolutionContext,
                              IHandleContext<TMessage> handleContext)
    {
        foreach (var asyncPreHandler in resolutionContext.IndirectPreHandlers.GetAsyncHandlers())
        {
            await (Task) asyncPreHandler.Handle(handleContext);
        }

        foreach (var asyncPreHandler in resolutionContext.PreHandlers.GetAsyncHandlers())
        {
            await (Task) asyncPreHandler.Handle(handleContext);
        }
    }
}