using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;
using LiteBus.Messaging.Workflows.Execution.Exceptions;

namespace LiteBus.Messaging.Workflows.Execution;

public class SingleSyncHandlerExecutionWorkflow<TMessage> : IExecutionWorkflow<TMessage, NoResult>
{
    public NoResult Execute(TMessage message, IMessageContext messageContext)
    {
        var handlers = messageContext.Handlers
                                     .Where(h => h.Descriptor.IsSynchronous())
                                     .ToList();

        if (handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(typeof(TMessage));
        }

        var handleContext = new HandleContext(message, default);

        try
        {
            messageContext.RunPreHandlers(handleContext).RunSynchronously();

            var handler = handlers.Single().Instance;

            handler.Handle(handleContext);

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

        return new NoResult();
    }
}