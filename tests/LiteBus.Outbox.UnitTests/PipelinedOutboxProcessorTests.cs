using System.Text;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies pipelined outbox processor leasing, publication, and terminal persistence.
/// </summary>
[Collection("Sequential")]
public sealed class PipelinedOutboxProcessorTests : LiteBusTestBase
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies that a single pass publishes every pending message through the fake transport.
    /// </summary>
    [Fact]
    public async Task PipelinedProcessor_WithConcurrencyOne_ShouldPublishAllMessages()
    {
        var store = new InMemoryOutboxStore();
        var transport = new TestMessageTransport();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");

        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            new StubOutboxDispatcher(transport),
            new OutboxProcessorOptions
            {
                BatchSize = 10,
                LeaseOwner = "publisher-test",
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            new ManualTimeProvider(BaseTime),
            Array.Empty<IProcessorEnvelopeHook>());

        for (var index = 0; index < 3; index++)
        {
            await store.AddAsync(new OutboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "orders.events.submitted",
                ContractVersion = 1,
                Payload = $"{{\"orderId\":\"{index}\"}}",
                CreatedAt = BaseTime.AddSeconds(index),
                Status = OutboxStatus.Pending,
                AttemptCount = 0
            }).ConfigureAwait(false);
        }

        var result = await processor.ProcessPendingAsync().ConfigureAwait(false);

        result.SucceededCount.Should().Be(3);
        transport.Published.Should().HaveCount(3);
        store.GetAll(OutboxStatus.Published).Should().HaveCount(3);
    }

    /// <summary>
    ///     Stub dispatcher that publishes through <see cref="TestMessageTransport" />.
    /// </summary>
    private sealed class StubOutboxDispatcher : IOutboxDispatcher
    {
        /// <summary>
        ///     The transport used to record publications.
        /// </summary>
        private readonly TestMessageTransport _transport;

        /// <summary>
        ///     Initializes a new instance of the <see cref="StubOutboxDispatcher" /> class.
        /// </summary>
        /// <param name="transport">The transport used to record publications.</param>
        public StubOutboxDispatcher(TestMessageTransport transport)
        {
            _transport = transport;
        }

        /// <inheritdoc />
        public Task DispatchAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return _transport.PublishAsync(new TransportPublishRequest
            {
                Destination = "tests.topic",
                Body = Encoding.UTF8.GetBytes(envelope.Payload)
            }, cancellationToken);
        }
    }
}