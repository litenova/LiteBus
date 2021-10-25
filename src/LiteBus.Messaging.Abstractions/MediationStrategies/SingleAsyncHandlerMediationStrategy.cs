using System;
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
                                                  IMessageContext messageContext)
        {
            if (messageContext.Handlers.Count > 1)
            {
                throw new MultipleHandlerFoundException(typeof(TMessage));
            }

            var handleContext = new HandleContext(message, _cancellationToken);

            try
            {
                foreach (var preHandler in messageContext.PreHandlers)
                {
                    await preHandler.Value.PreHandleAsync(handleContext);
                }

                var handler = messageContext.Handlers.Single().Value;

                var result = await (Task<TMessageResult>)handler!.Handle(handleContext);

                handleContext.MessageResult = result;
            
                foreach (var postHandler in messageContext.PostHandlers)
                {
                    await postHandler.Value.PostHandleAsync(handleContext);
                }

                return result;
            }
            catch (Exception e)
            {
                if (messageContext.ErrorHandlers.Count == 0)
                {
                    throw;
                }
                
                handleContext.SetException(e);

                foreach (var errorHandler in messageContext.ErrorHandlers)
                {
                    await errorHandler.Value.HandleErrorAsync(handleContext);
                }
            }

            return default;
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
                                  IMessageContext messageContext)
        {
            if (messageContext.Handlers.Count > 1)
            {
                throw new MultipleHandlerFoundException(typeof(TMessage));
            }

            var handleContext = new HandleContext(message, _cancellationToken);
            
            try
            {
                foreach (var preHandler in messageContext.PreHandlers)
                {
                    await preHandler.Value.PreHandleAsync(handleContext);
                }

                var handler = messageContext.Handlers.Single().Value;

                await (Task)handler!.Handle(handleContext);

                foreach (var postHandler in messageContext.PostHandlers)
                {
                    await postHandler.Value.PostHandleAsync(handleContext);
                }
            }
            catch (Exception e)
            {
                if (messageContext.ErrorHandlers.Count == 0)
                {
                    throw;
                }
                
                handleContext.SetException(e);

                foreach (var errorHandler in messageContext.ErrorHandlers)
                {
                    await errorHandler.Value.HandleErrorAsync(handleContext);
                }
            }
        }
    }
}