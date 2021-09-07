﻿using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.MediationStrategies
{
    public class AsyncBroadcastMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, Task>
    {
        private readonly CancellationToken _cancellationToken;

        public AsyncBroadcastMediationStrategy(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public async Task Mediate(TMessage message, IMessageContext messageContext)
        {
            var handleContext = new HandleContext(message, _cancellationToken);


            foreach (var preHandler in messageContext.PreHandlers)
            {
                await preHandler.Value.PreHandleAsync(handleContext);
            }

            foreach (var handler in messageContext.Handlers)
            {
                await (Task)handler.Value.Handle(handleContext);
            }

            foreach (var postHandler in messageContext.PostHandlers)
            {
                await postHandler.Value.PostHandleAsync(handleContext);
            }
        }
    }
}