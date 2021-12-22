using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Exceptions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Messaging.Abstractions.MediationStrategies;

public class SingleStreamHandlerMediationStrategy<TMessage, TMessageResult> :
    IMessageMediationStrategy<TMessage, IAsyncEnumerable<TMessageResult>> where TMessage : notnull
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
        var result = AsyncEnumerable.Empty<TMessageResult>();
        var exceptionThrown = false;

        await messageContext.RunPreHandlers(handleContext);

        try
        {
            var handler = messageContext.Handlers.Single().Value;

            result = (IAsyncEnumerable<TMessageResult>) handler!.Handle(handleContext);
        }
        catch (Exception e)
        {
            if (messageContext.ErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            await messageContext.RunErrorHandlers(handleContext);

            exceptionThrown = true;
        }

        await foreach (var messageResult in result.WithCancellation(_cancellationToken))
        {
            yield return messageResult;
        }

        if (!exceptionThrown)
        {
            handleContext.MessageResult = result;

            await messageContext.RunPostHandlers(handleContext);
        }
    }
}

public static class AsyncEnumerable
{
    /// <summary>
    ///     Creates an <see cref="IAsyncEnumerable{T}" /> which yields no results, similar to
    ///     <see cref="Enumerable.Empty{TResult}" />.
    /// </summary>
    public static IAsyncEnumerable<T> Empty<T>()
    {
        return EmptyAsyncEnumerator<T>.Instance;
    }

    private class EmptyAsyncEnumerator<T> : IAsyncEnumerator<T>, IAsyncEnumerable<T>
    {
        public static readonly EmptyAsyncEnumerator<T> Instance = new();

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return this;
        }

        public T Current => default;

        public ValueTask DisposeAsync()
        {
            return default;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            return new ValueTask<bool>(false);
        }
    }
}