using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions.MediationStrategies
{
    public class SingleAsyncHandlerMediationStrategy<TMessage, TMessageResult> :
        IMessageMediationStrategy<TMessage, Task<TMessageResult>>
    {
        private readonly CancellationToken _cancellationToken;

        public SingleAsyncHandlerMediationStrategy(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public async Task<TMessageResult> Mediate(TMessage message,
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

            var result = (Task<TMessageResult>)handler!.Handle(message, handleContext);

            foreach (var postHandleHook in context.PostHandleAsyncHooks)
            {
                await postHandleHook.Value.ExecuteAsync(message, handleContext);
            }

            return await result;
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

            await (Task)handler!.Handle(message, handleContext);

            foreach (var postHandleHook in context.PostHandleAsyncHooks)
            {
                await postHandleHook.Value.ExecuteAsync(message, handleContext);
            }
        }
    }
}