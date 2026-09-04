using System.Runtime.CompilerServices;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     A stream query whose source and override streams the test steers independently.
/// </summary>
internal sealed class SteeredStreamQuery : IStreamQuery<int>
{
    /// <summary>
    ///     Gets or sets the number of items the source stream yields before it is exhausted or faults.
    /// </summary>
    public int SourceItems { get; set; } = 3;

    /// <summary>
    ///     Gets or sets a value indicating whether the source stream faults after its items.
    /// </summary>
    public bool SourceFaults { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether a post-handler replaces the stream the caller enumerates.
    /// </summary>
    public bool ReplaceStream { get; set; }

    /// <summary>
    ///     Gets or sets the number of items the override stream yields before it is exhausted or faults.
    /// </summary>
    public int OverrideItems { get; set; } = 2;

    /// <summary>
    ///     Gets or sets a value indicating whether the override stream faults after its items.
    /// </summary>
    public bool OverrideFaults { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the source enumerator was disposed.
    /// </summary>
    public bool SourceDisposed { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the source enumerator had been disposed when post-handlers ran.
    /// </summary>
    public bool SourceDisposedBeforePostHandlers { get; set; }
}

/// <summary>
///     Streams the items the query asked for, faulting afterwards when it asked for that.
/// </summary>
internal sealed class SteeredStreamQueryHandler : IStreamQueryHandler<SteeredStreamQuery, int>
{
    /// <inheritdoc />
    public async IAsyncEnumerable<int> StreamAsync(
        SteeredStreamQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            for (var index = 0; index < query.SourceItems; index++)
            {
                await Task.Yield();
                yield return index;
            }

            if (query.SourceFaults)
            {
                throw new InvalidOperationException("the source stream faulted");
            }
        }
        finally
        {
            // Records that the pipeline disposed the enumerator, including when the caller stopped early.
            query.SourceDisposed = true;
        }
    }
}

/// <summary>
///     Replaces the stream the caller enumerates when the query asks for it.
/// </summary>
internal sealed class SteeredStreamOverridePostHandler : IStreamQueryPostHandler<SteeredStreamQuery, int>
{
    /// <inheritdoc />
    public Task PostHandleAsync(
        SteeredStreamQuery message,
        IAsyncEnumerable<int>? messageResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.SourceDisposedBeforePostHandlers = message.SourceDisposed;

        if (message.ReplaceStream)
        {
            AmbientExecutionContext.Current.MessageResult = OverrideStream(message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Streams the override items, faulting afterwards when the query asked for that.
    /// </summary>
    /// <param name="query">The query being mediated.</param>
    /// <returns>The stream the caller enumerates in place of the handler's.</returns>
    private static async IAsyncEnumerable<int> OverrideStream(SteeredStreamQuery query)
    {
        for (var index = 0; index < query.OverrideItems; index++)
        {
            await Task.Yield();
            yield return 100 + index;
        }

        if (query.OverrideFaults)
        {
            throw new InvalidOperationException("the override stream faulted");
        }
    }
}

/// <summary>
///     Observes how a <see cref="SteeredStreamQuery" /> mediation ended.
/// </summary>
internal sealed class SteeredStreamCompletionHandler : IQueryCompletionHandler<SteeredStreamQuery>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SteeredStreamCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SteeredStreamCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(
        MessageCompletionContext<SteeredStreamQuery> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("stream", context.AsUntyped()));

        return Task.CompletedTask;
    }
}

/// <summary>
///     Records that an error handler saw a stream fault, and recovers from it.
/// </summary>
/// <remarks>
///     Marking the fault handled is what lets enumeration end gracefully. An observing handler that leaves the outcome
///     unhandled lets the exception reach the caller, which matches every other strategy and is covered separately.
/// </remarks>
internal sealed class SteeredStreamErrorHandler : IQueryErrorHandler<SteeredStreamQuery>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StageOrderRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SteeredStreamErrorHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SteeredStreamErrorHandler(StageOrderRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<SteeredStreamQuery, object> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Add(context.Exception.Message);
        context.Outcome = MessageErrorOutcome.Handled;

        return Task.CompletedTask;
    }
}
