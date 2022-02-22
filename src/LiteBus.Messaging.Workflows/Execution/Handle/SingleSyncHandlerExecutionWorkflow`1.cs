using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;
using LiteBus.Messaging.Workflows.Execution.Exceptions;
using LiteBus.Messaging.Workflows.Extensions;

namespace LiteBus.Messaging.Workflows.Execution.Handle;

public class SingleSyncHandlerExecutionWorkflow<TMessage> : IExecutionWorkflow<TMessage, NoResult>
{
    public NoResult Execute(TMessage message, IResolutionContext resolutionContext)
    {
        var handlers = resolutionContext.Handlers
                                     .Where(h => h.Descriptor.ExecutionMode == ExecutionMode.Synchronous)
                                     .ToList();

        if (handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(typeof(TMessage));
        }

        var handleContext = new HandleContext(message);

        try
        {
            resolutionContext.RunSyncPreHandlers(handleContext);

            var handler = handlers.Single().Instance;

            handler.Handle(handleContext);

            resolutionContext.RunSyncPostHandlers(handleContext);
        }
        catch (Exception e)
        {
            if (resolutionContext.ErrorHandlers.Count + resolutionContext.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            resolutionContext.RunSyncErrorHandlers(handleContext);
        }

        return new NoResult();
    }
}