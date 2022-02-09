using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;
using LiteBus.Messaging.Workflows.Extensions;

namespace LiteBus.Messaging.Workflows.Execution;

public class AsyncBroadcastExecutionWorkflow<TMessage> : IExecutionWorkflow<TMessage, Task>
    where TMessage : notnull
{
    private readonly CancellationToken _cancellationToken;

    public AsyncBroadcastExecutionWorkflow(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public async Task Execute(TMessage message, IMessageContext messageContext)
    {
        var handlers = messageContext.Handlers
                                     .Where(h => h.Descriptor.ExecutionMode == ExecutionMode.Asynchronous)
                                     .ToList();

        var handleContext = new HandleContext(message, _cancellationToken);

        try
        {
            await messageContext.RunAsyncPreHandlers(handleContext);

            foreach (var handler in handlers)
            {
                await (Task) handler.Instance.Handle(handleContext);
            }

            await messageContext.RunAsyncPostHandlers(handleContext);
        }
        catch (Exception e)
        {
            if (messageContext.ErrorHandlers.Count + messageContext.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            await messageContext.RunAsyncErrorHandlers(handleContext);
        }
    }
}