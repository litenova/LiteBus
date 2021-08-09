using System.Threading;
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

        public async Task Mediate(TMessage message, IMessageContext context)
        {
            var handleContext = new HandleContext();
            handleContext.Data.Set(_cancellationToken);

            foreach (var preHandleHook in context.PreHandleAsyncHooks)
            {
                await preHandleHook.Value.ExecuteAsync(message, handleContext);
            }

            foreach (var handler in context.Handlers)
            {
                await (Task)handler.Value.Handle(message, handleContext);
            }

            foreach (var postHandleHook in context.PostHandleAsyncHooks)
            {
                await postHandleHook.Value.ExecuteAsync(message, handleContext);
            }
        }
    }
}