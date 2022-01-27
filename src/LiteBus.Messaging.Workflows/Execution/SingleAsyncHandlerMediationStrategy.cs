using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;
using LiteBus.Messaging.Workflows.Execution.Exceptions;

namespace LiteBus.Messaging.Workflows.Execution;

public class SingleAsyncHandlerMediationStrategy<TMessage, TMessageResult> :
    IExecutionWorkflow<TMessage, Task<TMessageResult>> where TMessage : notnull
{
    private readonly CancellationToken _cancellationToken;

    public SingleAsyncHandlerMediationStrategy(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public async Task<TMessageResult> Execute(TMessage message,
                                              IMessageContext messageContext)
    {
        if (messageContext.Handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(typeof(TMessage));
        }

        var handleContext = new HandleContext(message, _cancellationToken);
        TMessageResult result = default;

        try
        {
            await messageContext.RunPreHandlers(handleContext);

            var handler = messageContext.Handlers.Single().Value;

            result = await (Task<TMessageResult>) handler!.Handle(handleContext);

            handleContext.MessageResult = result;

            await messageContext.RunPostHandlers(handleContext);
        }
        catch (Exception e)
        {
            if (messageContext.ErrorHandlers.Count + messageContext.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            await messageContext.RunErrorHandlers(handleContext);
        }

        return result;
    }
}

public class SingleAsyncHandlerMediationStrategy<TMessage> : IExecutionWorkflow<TMessage, Task>
{
    private readonly CancellationToken _cancellationToken;

    public SingleAsyncHandlerMediationStrategy(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public async Task Execute(TMessage message,
                              IMessageContext messageContext)
    {
        if (messageContext.Handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(typeof(TMessage));
        }

        var handleContext = new HandleContext(message, _cancellationToken);

        try
        {
            await messageContext.RunPreHandlers(handleContext);

            var handler = messageContext.Handlers.Single().Value;

            await (Task) handler.Handle(handleContext);

            await messageContext.RunPostHandlers(handleContext);
        }
        catch (Exception e)
        {
            if (messageContext.ErrorHandlers.Count + messageContext.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            await messageContext.RunErrorHandlers(handleContext);
        }
    }
}