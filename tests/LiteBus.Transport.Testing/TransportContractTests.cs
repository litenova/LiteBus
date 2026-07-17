using System.Text;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Testing;

/// <summary>
///     Defines the transport-neutral behavior every LiteBus publisher and consumer adapter must satisfy.
/// </summary>
/// <remarks>
///     Derive one concrete xUnit class per adapter and return an isolated destination from
///     <see cref="CreateContextAsync" />. Each returned context must own every resource created for that test.
/// </remarks>
public abstract class TransportContractTests
{
    /// <summary>
    ///     Gets the maximum time allowed for an asynchronous broker delivery.
    /// </summary>
    protected virtual TimeSpan DeliveryTimeout => TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Creates an isolated publisher, consumer, and destination for one contract scenario.
    /// </summary>
    /// <param name="scenario">A broker-safe scenario name that can be included in the destination.</param>
    /// <returns>The context that owns the adapter resources for the scenario.</returns>
    protected abstract ValueTask<TransportContractContext> CreateContextAsync(string scenario);

    /// <summary>
    ///     Verifies that payload bytes and LiteBus headers survive a publish and consume round trip.
    /// </summary>
    /// <returns>A task that completes when the delivery is accepted.</returns>
    [Fact]
    public async Task PublishAsync_ThenConsume_PreservesPayloadAndHeaders()
    {
        var context = await CreateContextAsync("roundtrip").ConfigureAwait(false);

        try
        {
            var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            await context.Consumer.StartAsync(
                context.ConsumerOptions,
                async (message, cancellationToken) =>
                {
                    received.TrySetResult(message);
                    await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

            var messageId = Guid.NewGuid().ToString("D");
            var body = Encoding.UTF8.GetBytes($"litebus-transport-{messageId}");

            await context.Publisher.PublishAsync(CreateRequest(context, body, messageId)).ConfigureAwait(false);

            var message = await received.Task.WaitAsync(DeliveryTimeout).ConfigureAwait(false);

            Assert.Equal(body, message.Body.ToArray());
            Assert.Equal("litebus.transport.contract", TransportHeaderValues.GetString(message.Headers, TransportHeaders.ContractName));
            Assert.Equal(1, TransportHeaderValues.GetInt32(message.Headers, TransportHeaders.ContractVersion));
            Assert.Equal(messageId, TransportHeaderValues.GetString(message.Headers, TransportHeaders.MessageId));
            Assert.Equal("transport-contract", TransportHeaderValues.GetString(message.Headers, TransportHeaders.CorrelationId));

            await context.Consumer.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies that returning a delivery to the queue causes another delivery before it is accepted.
    /// </summary>
    /// <returns>A task that completes after the redelivered message is accepted.</returns>
    [Fact]
    public async Task ReturnToQueueAsync_ThenConsume_RedeliversMessage()
    {
        var context = await CreateContextAsync("redelivery").ConfigureAwait(false);

        try
        {
            var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var deliveryCount = 0;

            await context.Consumer.StartAsync(
                context.ConsumerOptions,
                async (message, cancellationToken) =>
                {
                    if (Interlocked.Increment(ref deliveryCount) == 1)
                    {
                        await message.ReturnToQueueAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
                    received.TrySetResult(message);
                }).ConfigureAwait(false);

            var body = Encoding.UTF8.GetBytes("litebus-transport-redelivery");
            await context.Publisher.PublishAsync(CreateRequest(context, body, Guid.NewGuid().ToString("D"))).ConfigureAwait(false);

            var message = await received.Task.WaitAsync(DeliveryTimeout).ConfigureAwait(false);

            Assert.True(Volatile.Read(ref deliveryCount) >= 2);
            Assert.Equal(body, message.Body.ToArray());

            await context.Consumer.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies that a publisher observes a cancellation requested before publication begins.
    /// </summary>
    /// <returns>A task that completes when cancellation is observed.</returns>
    [Fact]
    public async Task PublishAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        var context = await CreateContextAsync("cancellation").ConfigureAwait(false);

        try
        {
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            var request = CreateRequest(
                context,
                Encoding.UTF8.GetBytes("litebus-transport-cancelled"),
                Guid.NewGuid().ToString("D"));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => context.Publisher.PublishAsync(request, cancellationSource.Token)).ConfigureAwait(false);
        }
        finally
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates a publication request containing the canonical metadata asserted by the contract suite.
    /// </summary>
    /// <param name="context">The adapter context supplying destination and route values.</param>
    /// <param name="body">The payload bytes to publish.</param>
    /// <param name="messageId">The stable LiteBus message identifier.</param>
    /// <returns>The request passed to the adapter publisher.</returns>
    private static TransportPublishRequest CreateRequest(
        TransportContractContext context,
        ReadOnlyMemory<byte> body,
        string messageId)
    {
        return new TransportPublishRequest
        {
            Destination = context.PublishDestination,
            Route = context.PublishRoute,
            Body = body,
            MessageId = messageId,
            CorrelationId = "transport-contract",
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.MessageId] = messageId,
                [TransportHeaders.ContractName] = "litebus.transport.contract",
                [TransportHeaders.ContractVersion] = 1,
                [TransportHeaders.CorrelationId] = "transport-contract"
            }
        };
    }
}
