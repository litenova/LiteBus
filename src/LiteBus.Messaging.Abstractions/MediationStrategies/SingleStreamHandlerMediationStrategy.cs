using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

        public IAsyncEnumerable<TMessageResult> Mediate(TMessage message,
                                                        IMessageContext context)
        {
            if (context.Handlers.Count > 1)
            {
                throw new MultipleHandlerFoundException(typeof(TMessage));
            }

            var handleContext = new HandleContext();
            handleContext.Data.Set(_cancellationToken);

            var handler = context.Handlers.Single().Value;

            var result = (IAsyncEnumerable<TMessageResult>)handler!.Handle(message, handleContext);

            return result;
        }
    }
}