using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;
using LiteBus.Messaging.Workflows.Extensions;

namespace LiteBus.Messaging.Workflows.Execution.Handle;

public class SyncBroadcastExecutionWorkflow<TMessage> : IExecutionWorkflow<TMessage, NoResult>
    where TMessage : notnull
{
    public NoResult Execute(TMessage message, IResolutionContext resolutionContext)
    {
        var handlers = resolutionContext.Handlers
                                     .Where(h => h.Descriptor.ExecutionMode == ExecutionMode.Synchronous)
                                     .ToList();

        var handleContext = new HandleContext(message);

        try
        {
            resolutionContext.RunSyncPreHandlers(handleContext);

            foreach (var handler in handlers)
            {
                handler.Instance.Handle(handleContext);
            }

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