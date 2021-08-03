using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions.MediationStrategies
{
    public class SingleAsyncHandlerMessageMediationStrategy<TMessage, TMessageResult> :
        IMessageMediationStrategy<TMessage, Task<TMessageResult>>
    {
        private readonly CancellationToken _cancellationToken;

        public SingleAsyncHandlerMessageMediationStrategy(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public async Task<TMessageResult> Mediate(TMessage message,
                                                  IMessageContext<TMessage, Task<TMessageResult>> context)
        {
            if (context.Handlers.Count > 1)
            {
                throw new MultipleHandlerFoundException(typeof(TMessage));
            }

            foreach (var preHandleHook in context.PreHandleHooks)
            {
                await preHandleHook.Value.ExecuteAsync(message, _cancellationToken);
            }

            var handler = context.Handlers
                                 .Single()
                                 .Value as IAsyncMessageHandler<TMessage, TMessageResult>;

            var result = await handler!.HandleAsync(message, _cancellationToken);

            foreach (var postHandleHook in context.PostHandleHooks)
            {
                await postHandleHook.Value.ExecuteAsync(message, _cancellationToken);
            }

            return result;
        }
    }

    public class SingleAsyncHandlerMessageMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, Task>
    {
        private readonly CancellationToken _cancellationToken;

        public SingleAsyncHandlerMessageMediationStrategy(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public async Task Mediate(TMessage message,
                                  IMessageContext<TMessage, Task> context)
        {
            if (context.Handlers.Count > 1)
            {
                throw new MultipleHandlerFoundException(typeof(TMessage));
            }

            foreach (var preHandleHook in context.PreHandleHooks)
            {
                await preHandleHook.Value.ExecuteAsync(message, _cancellationToken);
            }

            var handler = context.Handlers
                                 .Single()
                                 .Value as IAsyncMessageHandler<TMessage>;

            await handler!.HandleAsync(message, _cancellationToken);

            foreach (var postHandleHook in context.PostHandleHooks)
            {
                await postHandleHook.Value.ExecuteAsync(message, _cancellationToken);
            }
        }
    }
}