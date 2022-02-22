using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;
using LiteBus.Messaging.Workflows.Execution.Exceptions;
using LiteBus.Messaging.Workflows.Extensions;

namespace LiteBus.Messaging.Workflows.Execution.Handle;

public class SingleAsyncHandlerExecutionWorkflow<TMessage> : IExecutionWorkflow<TMessage, Task>
{
    private readonly CancellationToken _cancellationToken;

    public SingleAsyncHandlerExecutionWorkflow(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public async Task Execute(TMessage message,
                              IResolutionContext resolutionContext)
    {
        var handlers = resolutionContext.Handlers
                                     .Where(h => h.Descriptor.ExecutionMode == ExecutionMode.Asynchronous)
                                     .ToList();

        if (handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(typeof(TMessage));
        }

        var handleContext = new HandleContext(message);
        handleContext.Data.Set(_cancellationToken);

        try
        {
            await resolutionContext.RunAsyncPreHandlers(handleContext);

            var handler = handlers.Single().Instance;

            await (Task) handler.Handle(handleContext);

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