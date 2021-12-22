using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Messaging.Abstractions.MediationStrategies;

public class AsyncBroadcastMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, Task>
    where TMessage : notnull
{
    private readonly CancellationToken _cancellationToken;

    public AsyncBroadcastMediationStrategy(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public async Task Mediate(TMessage message, IMessageContext messageContext)
    {
        var handleContext = new HandleContext(message, _cancellationToken);

        await messageContext.RunPreHandlers(handleContext);

        try
        {
            foreach (var handler in messageContext.Handlers)
            {
                await (Task) handler.Value.Handle(handleContext);
            }
        }
        catch (Exception e)
        {
            if (messageContext.ErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            await messageContext.RunErrorHandlers(handleContext);

            return;
        }

        await messageContext.RunPostHandlers(handleContext);
    }
}