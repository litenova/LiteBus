using System.Collections.Generic;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an asynchronous message handler that yields results in the form of an asynchronous stream of
///     <see cref="IAsyncEnumerable{TMessageResult}" />.
/// </summary>
/// <typeparam name="TMessage">Defines the type of message that this handler is designed to process.</typeparam>
/// <typeparam name="TMessageResult">Specifies the type of results that the handler produces for each processed message.</typeparam>
/// <remarks>
///     Handlers implementing this interface cater to scenarios where the processing of a single message results in a
///     series of outcomes, which can be streamed asynchronously to the caller. This can be beneficial in cases such as
///     processing large data sets, real-time data streaming, or any scenario where immediate processing and feedback of
///     each item in a data set is essential.
/// </remarks>
public interface IStreamMessageHandler<in TMessage, out TMessageResult> : IMessageHandler<TMessage, IAsyncEnumerable<TMessageResult>> where TMessage : notnull
{
    /// <summary>
    ///     Provides a non-generic entry point for the handler, wrapping the generic asynchronous handling method. This allows
    ///     callers to interact with the handler in a non-generic manner while still benefiting from asynchronous streamed
    ///     results.
    /// </summary>
    /// <param name="message">The message that requires processing.</param>
    /// <returns>
    ///     An asynchronous stream of results, each represented by <see cref="TMessageResult" />, produced from handling
    ///     the provided message.
    /// </returns>
    IAsyncEnumerable<TMessageResult> IMessageHandler<TMessage, IAsyncEnumerable<TMessageResult>>.Handle(TMessage message)
    {
        return StreamAsync(message, AmbientExecutionContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Processes the given message asynchronously, yielding a stream of results. Each item in the stream represents an
    ///     outcome or a part of the overall result from handling the message.
    /// </summary>
    /// <param name="message">The input message that the handler processes.</param>
    /// <param name="cancellationToken">
    ///     A token that can be used by the caller to request the cancellation of the asynchronous
    ///     operation, allowing for responsive applications and optimized resource utilization.
    /// </param>
    /// <returns>
    ///     An asynchronous enumerable stream of results. This stream can be consumed using async enumerators, enabling
    ///     the processing of each item as it becomes available.
    /// </returns>
    /// <remarks>
    ///     Implementers should provide the logic for processing the message and producing the streamed results within this
    ///     method. The asynchronous nature of the returned stream ensures that results can be processed and consumed as they
    ///     become available, without the need to wait for the entire operation to complete.
    /// </remarks>
    IAsyncEnumerable<TMessageResult> StreamAsync(TMessage message, CancellationToken cancellationToken = default);
}