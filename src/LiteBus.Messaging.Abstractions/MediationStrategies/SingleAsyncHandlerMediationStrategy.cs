using System.Linq;
using System.Threading;
using LiteBus.Messaging.Abstractions.Exceptions;
using MorseCode.ITask;

namespace LiteBus.Messaging.Abstractions.MediationStrategies
{
    public class SingleAsyncHandlerMediationStrategy<TMessage, TMessageResult> :
        IMessageMediationStrategy<TMessage, ITask<TMessageResult>>
    {
        private readonly CancellationToken _cancellationToken;

        public SingleAsyncHandlerMediationStrategy(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public async ITask<TMessageResult> Mediate(TMessage message,
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

            var result = (ITask<TMessageResult>)handler!.Handle(message, handleContext);

            foreach (var postHandleHook in context.PostHandleAsyncHooks)
            {
                await postHandleHook.Value.ExecuteAsync(message, handleContext);
            }

            return await result;
        }
    }

    public class SingleAsyncHandlerMessageMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, ITask>
    {
        private readonly CancellationToken _cancellationToken;

        public SingleAsyncHandlerMessageMediationStrategy(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public async ITask Mediate(TMessage message,
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

            await (ITask)handler!.Handle(message, handleContext);

            foreach (var postHandleHook in context.PostHandleAsyncHooks)
            {
                await postHandleHook.Value.ExecuteAsync(message, handleContext);
            }
        }
    }
}