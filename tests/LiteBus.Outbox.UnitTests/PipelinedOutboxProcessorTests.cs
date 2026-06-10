using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

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
        var transport = new FakeMessageTransport();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);

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
            Array.Empty<LiteBus.Orchestration.Abstractions.IProcessorEnvelopeHook>());

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
            });
        }

        var result = await processor.ProcessPendingAsync();

        result.SucceededCount.Should().Be(3);
        transport.Published.Should().HaveCount(3);
        store.GetAll(OutboxStatus.Published).Should().HaveCount(3);
    }

    /// <summary>
    ///     Stub dispatcher that publishes through <see cref="FakeMessageTransport" />.
    /// </summary>
    private sealed class StubOutboxDispatcher : IOutboxDispatcher
    {
        /// <summary>
        ///     The transport used to record publications.
        /// </summary>
        private readonly FakeMessageTransport _transport;

        /// <summary>
        ///     Initializes a new instance of the <see cref="StubOutboxDispatcher" /> class.
        /// </summary>
        /// <param name="transport">The transport used to record publications.</param>
        public StubOutboxDispatcher(FakeMessageTransport transport)
        {
            _transport = transport;
        }

        /// <inheritdoc />
        public Task DispatchAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return _transport.PublishAsync(new LiteBus.Transport.Abstractions.TransportPublishRequest
            {
                Destination = "tests.topic",
                Body = System.Text.Encoding.UTF8.GetBytes(envelope.Payload)
            }, cancellationToken);
        }
    }
}
