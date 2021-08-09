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
                                                              IMessageContext context)
        {
            if (context.Handlers.Count > 1)
            {
                throw new MultipleHandlerFoundException(typeof(TMessage));
            }

            var handleContext = new HandleContext();
            handleContext.Data.Set(_cancellationToken);

            foreach (var preHandleHook in context.PreHandleAsyncHooks)
            {
                await preHandleHook.Value.ExecuteAsync(message, handleContext);
            }

            var handler = context.Handlers.Single().Value;

            var result = (IAsyncEnumerable<TMessageResult>)handler!.Handle(message, handleContext);

            await foreach (var messageResult in result.WithCancellation(_cancellationToken))
            {
                yield return messageResult;
            }
        }
    }
}