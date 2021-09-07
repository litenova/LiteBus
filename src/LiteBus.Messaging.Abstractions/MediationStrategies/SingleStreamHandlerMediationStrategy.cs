using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions.MediationStrategies
{
    public class SingleStreamHandlerMediationStrategy<TMessage, TMessageResult> :
        IMessageMediationStrategy<TMessage, IAsyncEnumerable<TMessageResult>>
    {
        private readonly CancellationToken _cancellationToken;

        public SingleStreamHandlerMediationStrategy(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public async IAsyncEnumerable<TMessageResult> Mediate(TMessage message,
                                                              IMessageContext messageContext)
        {
            if (messageContext.Handlers.Count > 1)
            {
                throw new MultipleHandlerFoundException(typeof(TMessage));
            }

            var handleContext = new HandleContext(message, _cancellationToken);

            foreach (var preHandler in messageContext.PreHandlers)
            {
                await preHandler.Value.PreHandleAsync(handleContext);
            }

            var handler = messageContext.Handlers.Single().Value;

            var result = (IAsyncEnumerable<TMessageResult>)handler!.Handle(handleContext);

            await foreach (var messageResult in result.WithCancellation(_cancellationToken))
            {
                yield return messageResult;
            }

            handleContext.MessageResult = result;
            
            foreach (var postHandler in messageContext.PostHandlers)
            {
                await postHandler.Value.PostHandleAsync(handleContext);
            }
        }
    }
}