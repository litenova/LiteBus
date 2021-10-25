using System;
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
            IAsyncEnumerable<TMessageResult> result = AsyncEnumerable.Empty<TMessageResult>();
            var exceptionThrown = false;

            foreach (var preHandler in messageContext.PreHandlers)
            {
                await preHandler.Value.PreHandleAsync(handleContext);
            }

            try
            {
                var handler = messageContext.Handlers.Single().Value;

                result = (IAsyncEnumerable<TMessageResult>)handler!.Handle(handleContext);
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

                exceptionThrown = true;
            }

            await foreach (var messageResult in result.WithCancellation(_cancellationToken))
            {
                yield return messageResult;
            }

            if (!exceptionThrown)
            {
                handleContext.MessageResult = result;
            
                foreach (var postHandler in messageContext.PostHandlers)
                {
                    await postHandler.Value.PostHandleAsync(handleContext);
                }
            }
        }
    }

    public static class AsyncEnumerable
    {
        /// <summary>
        /// Creates an <see cref="IAsyncEnumerable{T}"/> which yields no results, similar to <see cref="Enumerable.Empty{TResult}"/>.
        /// </summary>
        public static IAsyncEnumerable<T> Empty<T>() => EmptyAsyncEnumerator<T>.Instance;

        private class EmptyAsyncEnumerator<T> : IAsyncEnumerator<T>, IAsyncEnumerable<T>
        {
            public static readonly EmptyAsyncEnumerator<T> Instance = new EmptyAsyncEnumerator<T>();
            public T Current => default;
            public ValueTask DisposeAsync() => default;
            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return this;
            }
            public ValueTask<bool> MoveNextAsync() => new ValueTask<bool>(false);
        }
    }
}