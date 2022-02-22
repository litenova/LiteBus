using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;
using LiteBus.Messaging.Workflows.Extensions;

namespace LiteBus.Messaging.Workflows.Execution.Handle;

public class AsyncBroadcastExecutionWorkflow<TMessage> : IExecutionWorkflow<TMessage, Task>
    where TMessage : notnull
{
    private readonly CancellationToken _cancellationToken;

    public AsyncBroadcastExecutionWorkflow(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public async Task Execute(TMessage message, IResolutionContext resolutionContext)
    {
        var handlers = resolutionContext.Handlers
                                     .Where(h => h.Descriptor.ExecutionMode == ExecutionMode.Asynchronous)
                                     .ToList();

        var handleContext = new HandleContext(message);
        handleContext.Data.Set(_cancellationToken);

        try
        {
            await resolutionContext.RunAsyncPreHandlers(handleContext);

            foreach (var handler in handlers)
            {
                await (Task) handler.Instance.Handle(handleContext);
            }

            await resolutionContext.RunAsyncPostHandlers(handleContext);
        }
        catch (Exception e)
        {
            if (resolutionContext.ErrorHandlers.Count + resolutionContext.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            await resolutionContext.RunAsyncErrorHandlers(handleContext);
        }
    }
}