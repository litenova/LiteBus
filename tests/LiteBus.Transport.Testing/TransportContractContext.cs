using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Testing;

/// <summary>
///     Owns the publisher, consumer, endpoint settings, and cleanup callback for one transport contract test.
/// </summary>
public sealed class TransportContractContext : IAsyncDisposable
{
    /// <summary>
    ///     Releases adapter resources created for this context.
    /// </summary>
    private Func<ValueTask>? _disposeAsync;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportContractContext" /> class.
    /// </summary>
    /// <param name="publisher">The publisher under test.</param>
    /// <param name="consumer">The consumer under test.</param>
    /// <param name="consumerOptions">The endpoint settings used to start the consumer.</param>
    /// <param name="publishDestination">The destination passed to the publisher.</param>
    /// <param name="disposeAsync">The callback that stops and releases all resources created for the context.</param>
    /// <param name="publishRoute">The optional route passed to the publisher.</param>
    public TransportContractContext(
        ITransportPublisher publisher,
        IMessageConsumer consumer,
        TransportConsumerOptions consumerOptions,
        string publishDestination,
        Func<ValueTask> disposeAsync,
        string? publishRoute = null)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(consumerOptions);
        ArgumentNullException.ThrowIfNull(disposeAsync);
        ArgumentNullException.ThrowIfNull(publishDestination);

        Publisher = publisher;
        Consumer = consumer;
        ConsumerOptions = consumerOptions;
        PublishDestination = publishDestination;
        PublishRoute = publishRoute;
        _disposeAsync = disposeAsync;
    }

    /// <summary>
    ///     Gets the publisher under test.
    /// </summary>
    public ITransportPublisher Publisher { get; }

    /// <summary>
    ///     Gets the consumer under test.
    /// </summary>
    public IMessageConsumer Consumer { get; }

    /// <summary>
    ///     Gets the endpoint settings used to start the consumer.
    /// </summary>
    public TransportConsumerOptions ConsumerOptions { get; }

    /// <summary>
    ///     Gets the destination passed to the publisher.
    /// </summary>
    public string PublishDestination { get; }

    /// <summary>
    ///     Gets the optional route passed to the publisher.
    /// </summary>
    public string? PublishRoute { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var disposeAsync = Interlocked.Exchange(ref _disposeAsync, null);

        if (disposeAsync is not null)
        {
            await disposeAsync().ConfigureAwait(false);
        }
    }
}
