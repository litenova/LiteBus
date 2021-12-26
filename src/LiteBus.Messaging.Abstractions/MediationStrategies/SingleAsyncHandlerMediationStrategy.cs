using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Exceptions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Messaging.Abstractions.MediationStrategies;

public class SingleAsyncHandlerMediationStrategy<TMessage, TMessageResult> :
    IMessageMediationStrategy<TMessage, Task<TMessageResult>> where TMessage : notnull
{
    private readonly CancellationToken _cancellationToken;

    public SingleAsyncHandlerMediationStrategy(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public async Task<TMessageResult> Mediate(TMessage message,
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
            if (messageContext.ErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            await messageContext.RunErrorHandlers(handleContext);
        }

        return result;
    }
}

public class SingleAsyncHandlerMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, Task>
{
    private readonly CancellationToken _cancellationToken;

    public SingleAsyncHandlerMediationStrategy(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public async Task Mediate(TMessage message,
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
            if (messageContext.ErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            await messageContext.RunErrorHandlers(handleContext);
        }
    }
}