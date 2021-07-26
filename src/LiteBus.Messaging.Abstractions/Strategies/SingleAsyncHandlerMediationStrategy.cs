using System.Linq;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions.Strategies
{
    public class SingleAsyncHandlerMediationStrategy<TMessage, TMessageResult> :
        IMediationStrategy<ICancellableMessage<TMessage>, Task<TMessageResult>>
    {
        public async Task<TMessageResult> Mediate(ICancellableMessage<TMessage> message,
                                                  IMessageContext<ICancellableMessage<TMessage>, Task<TMessageResult>> context)
        {
            if (context.Handlers.Count > 1)
            {
                throw new MultipleHandlerFoundException(typeof(TMessage));
            }

            foreach (var preHandleHook in context.PreHandleHooks)
            {
                await preHandleHook.Value.ExecuteAsync(message);
            }

            var result = await context.Handlers.Single().Value.Handle(message);

            foreach (var postHandleHook in context.PostHandleHooks)
            {
                await postHandleHook.Value.ExecuteAsync(message);
            }

            return result;
        }
    }
}