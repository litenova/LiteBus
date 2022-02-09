using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;
using LiteBus.Messaging.Workflows.Extensions;

namespace LiteBus.Messaging.Workflows.Execution;

public class SyncBroadcastExecutionWorkflow<TMessage> : IExecutionWorkflow<TMessage, NoResult>
    where TMessage : notnull
{
    public NoResult Execute(TMessage message, IMessageContext messageContext)
    {
        var handlers = messageContext.Handlers
                                     .Where(h => h.Descriptor.ExecutionMode == ExecutionMode.Synchronous)
                                     .ToList();

        var handleContext = new HandleContext(message, default);

        try
        {
            messageContext.RunSyncPreHandlers(handleContext);

            foreach (var handler in handlers)
            {
                handler.Instance.Handle(handleContext);
            }

            messageContext.RunSyncPostHandlers(handleContext);
        }
        catch (Exception e)
        {
            if (messageContext.ErrorHandlers.Count + messageContext.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            messageContext.RunSyncErrorHandlers(handleContext);
        }

        return new NoResult();
    }
}