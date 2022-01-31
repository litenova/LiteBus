using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;
using LiteBus.Messaging.Workflows.Execution.Exceptions;

namespace LiteBus.Messaging.Workflows.Execution;

public class SingleSyncHandlerExecutionWorkflow<TMessage, TMessageResult> :
    IExecutionWorkflow<TMessage, TMessageResult> where TMessage : notnull
{
    public TMessageResult Execute(TMessage message,
                                  IMessageContext messageContext)
    {
        var handlers = messageContext.Handlers
                                     .Where(h => h.Descriptor.IsSynchronous())
                                     .ToList();

        if (handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(typeof(TMessage));
        }

        var handleContext = new HandleContext(message, default);
        TMessageResult result = default;

        try
        {
            messageContext.RunPreHandlers(handleContext).RunSynchronously();

            var handler = handlers.Single().Instance;

            result = (TMessageResult) handler!.Handle(handleContext);

            handleContext.MessageResult = result;

            messageContext.RunPostHandlers(handleContext).RunSynchronously();
        }
        catch (Exception e)
        {
            if (messageContext.ErrorHandlers.Count + messageContext.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            messageContext.RunErrorHandlers(handleContext).RunSynchronously();
        }

        return result;
    }
}