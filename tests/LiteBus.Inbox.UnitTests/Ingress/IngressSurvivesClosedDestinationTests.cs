using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Transport;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox.UnitTests.Ingress;

/// <summary>
///     Verifies the ingress loop survives a completed in-memory destination channel.
/// </summary>
public sealed class IngressSurvivesClosedDestinationTests
{
    /// <summary>
    ///     Verifies completing the destination with an in-flight delivery does not fault the ingress loop.
    /// </summary>
    /// <returns>A task that completes when the survival assertion succeeds.</returns>
    [Fact]
    public async Task ExecuteAsync_WhenDestinationCompletesWithInflightDelivery_ShouldSurviveAndStopCleanly()
    {
        var destination = "ingress-closed-" + Guid.NewGuid().ToString("N");
        var broker = new InMemoryTransportBroker();
        var transportConsumer = new InMemoryConsumer(broker);
        var enteredInbox = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInbox = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inbox = new BlockingThrowingInbox(enteredInbox, releaseInbox, new IOException("transient ingress failure"));

        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ProbeCommand>("probe.command");

        var options = new TransportInboxIngressOptions
        {
            Destination = destination,
            RequeueOnFailure = true,
            Safety = new TransportInboxIngressSafetyOptions
            {
                RequireStableIdentity = false
            }
        };
        var handler = new TransportInboxIngressHandler(
            inbox,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            options);
        var ingress = new TransportInboxIngressConsumer(
            transportConsumer,
            handler,
            options,
            new TransportInboxIngressHostOptions
            {
                Enabled = true,
                RetryPollInterval = TimeSpan.Zero
            },
            NullLogger<TransportInboxIngressConsumer>.Instance);

        using var stoppingSource = new CancellationTokenSource();
        var execution = ingress.ExecuteAsync(stoppingSource.Token);

        var publisher = new InMemoryPublisher(broker, new TransportCircuitBreakerRegistry());
        var messageId = Guid.NewGuid().ToString("D");
        await publisher.PublishAsync(new TransportPublishRequest
        {
            Destination = destination,
            Body = """{"value":1}"""u8.ToArray(),
            MessageId = messageId,
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = "probe.command",
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.MessageId] = messageId
            }
        }).ConfigureAwait(false);

        await enteredInbox.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        broker.Dispose();
        releaseInbox.TrySetResult();

        var settleUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
        while (!execution.IsCompleted && DateTime.UtcNow < settleUntil)
        {
            await Task.Delay(25).ConfigureAwait(false);
        }

        execution.IsFaulted.Should().BeFalse();
        execution.IsCompleted.Should().BeFalse();

        await stoppingSource.CancelAsync().ConfigureAwait(false);
        await execution.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        var stop = () => transportConsumer.StopAsync();
        await stop.Should().NotThrowAsync().ConfigureAwait(false);

        await transportConsumer.DisposeAsync().ConfigureAwait(false);
        ingress.Dispose();
        broker.Dispose();
    }

    /// <summary>
    ///     Probe command accepted through ingress in survival tests.
    /// </summary>
    private sealed record ProbeCommand(int Value);

    /// <summary>
    ///     Blocks the first accept until released, then throws so ingress requeues the delivery.
    /// </summary>
    private sealed class BlockingThrowingInbox : IInbox
    {
        /// <summary>
        ///     Gets the gate signalled when an accept reaches the inbox.
        /// </summary>
        private readonly TaskCompletionSource _entered;

        /// <summary>
        ///     Gets the gate that releases the blocked accept.
        /// </summary>
        private readonly TaskCompletionSource _release;

        /// <summary>
        ///     Gets the failure thrown once the blocked accept is released.
        /// </summary>
        private readonly Exception _failure;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BlockingThrowingInbox" /> class.
        /// </summary>
        /// <param name="entered">The gate signalled when an accept reaches the inbox.</param>
        /// <param name="release">The gate that releases the blocked accept.</param>
        /// <param name="failure">The failure thrown once the blocked accept is released.</param>
        public BlockingThrowingInbox(TaskCompletionSource entered, TaskCompletionSource release, Exception failure)
        {
            _entered = entered;
            _release = release;
            _failure = failure;
        }

        /// <inheritdoc />
        public Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(InboxAcceptItem<TMessage> item, CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public async Task<InboxReceipt> AcceptAsync(InboxAcceptItem item, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw _failure;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(IReadOnlyList<InboxAcceptItem> items, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
