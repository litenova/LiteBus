#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Mediates the handling of a message by invoking a single asynchronous stream handler.
/// This strategy ensures that only one handler processes the message and produces a stream of results.
/// </summary>
/// <typeparam name="TMessage">Type of the message being handled.</typeparam>
/// <typeparam name="TMessageResult">Type of the results returned by the message handler.</typeparam>
public sealed class SingleStreamHandlerMediationStrategy<TMessage, TMessageResult> : IMessageMediationStrategy<TMessage, IAsyncEnumerable<TMessageResult>> where TMessage : notnull
{
    private readonly CancellationToken _cancellationToken;

    public SingleStreamHandlerMediationStrategy(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public async IAsyncEnumerable<TMessageResult> Mediate(TMessage message, IMessageDependencies messageDependencies)
    {
        if (messageDependencies.Handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(typeof(TMessage), messageDependencies.Handlers.Count);
        }

        IAsyncEnumerator<TMessageResult>? messageResultAsyncEnumerator = null;

        IAsyncEnumerable<TMessageResult>? messageResultAsyncEnumerable = null;

        try
        {
            await messageDependencies.RunAsyncPreHandlers(message);

            var handler = messageDependencies.Handlers.Single().Value;

            messageResultAsyncEnumerable = (IAsyncEnumerable<TMessageResult>) handler!.Handle(message);

            // ReSharper disable once PossibleMultipleEnumeration
            messageResultAsyncEnumerator = messageResultAsyncEnumerable.GetAsyncEnumerator(_cancellationToken);
        }
        catch (Exception exception)
        {
            if (messageDependencies.ErrorHandlers.Count + messageDependencies.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            await messageDependencies.RunErrorHandlers(message, messageResultAsyncEnumerable, exception);
        }

        messageResultAsyncEnumerable ??= Empty<TMessageResult>();

        // ReSharper disable once PossibleMultipleEnumeration
        messageResultAsyncEnumerator ??= messageResultAsyncEnumerable.GetAsyncEnumerator(_cancellationToken);

        TMessageResult? item = default;
        var hasResult = true;

        while (hasResult)
        {
            try
            {
                hasResult = await messageResultAsyncEnumerator.MoveNextAsync().ConfigureAwait(false);

                item = hasResult ? messageResultAsyncEnumerator.Current : default;
            }
            catch (Exception exception)
            {
                if (messageDependencies.ErrorHandlers.Count + messageDependencies.IndirectErrorHandlers.Count == 0)
                {
                    throw;
                }

                await messageDependencies.RunErrorHandlers(message, messageResultAsyncEnumerable, exception);
            }

            if (item != null)
            {
                yield return item;
            }
        }

        try
        {
            await messageDependencies.RunPostHandlers(message, messageResultAsyncEnumerable);
        }
        catch (Exception exception)
        {
            if (messageDependencies.ErrorHandlers.Count + messageDependencies.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            await messageDependencies.RunErrorHandlers(message, messageResultAsyncEnumerable, exception);
        }
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    // https://github.com/dotnet/runtime/issues/1128#issuecomment-571624647
    private static async IAsyncEnumerable<T> Empty<T>()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        yield break;
    }
}