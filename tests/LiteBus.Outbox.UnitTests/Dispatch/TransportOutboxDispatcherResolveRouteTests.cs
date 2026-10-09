using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Abstractions.Exceptions;
using LiteBus.Outbox.Dispatch;
using LiteBus.Testing;

namespace LiteBus.Outbox.UnitTests.Dispatch;

/// <summary>
///     Verifies route resolver precedence in <see cref="TransportOutboxDispatcher" />.
/// </summary>
public sealed class TransportOutboxDispatcherResolveRouteTests
{
    /// <summary>
    ///     Verifies the configured route resolver wins over the envelope topic.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_WithResolveRouteAndTopic_ShouldUseResolveRoute()
    {
        var transport = new TestMessageTransport();
        var dispatcher = CreateDispatcher(transport, _ => "routes.custom");

        await dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.order-submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1,
            Topic = "orders.priority"
        }).ConfigureAwait(false);

        transport.Published.Should().ContainSingle().Which.Route.Should().Be("routes.custom");
    }

    /// <summary>
    ///     Verifies the envelope topic is used when no route resolver is configured.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_WithTopicAndNoResolveRoute_ShouldUseTopic()
    {
        var transport = new TestMessageTransport();
        var dispatcher = CreateDispatcher(transport, null);

        await dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.order-submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1,
            Topic = "orders.priority"
        }).ConfigureAwait(false);

        transport.Published.Should().ContainSingle().Which.Route.Should().Be("orders.priority");
    }

    /// <summary>
    ///     Verifies an empty route resolver result fails the dispatch and publishes nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DispatchAsync_WithEmptyResolveRouteResult_ShouldThrowLiteBusDispatchException(string? route)
    {
        var transport = new TestMessageTransport();
        var dispatcher = CreateDispatcher(transport, _ => route!);
        var messageId = Guid.NewGuid();

        var act = () => dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.order-submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1,
            Topic = "orders.priority"
        });

        var failure = await act.Should().ThrowAsync<LiteBusDispatchException>().ConfigureAwait(false);
        failure.Which.Message.Should().Contain(messageId.ToString("D"));
        transport.Published.Should().BeEmpty();
    }

    /// <summary>
    ///     Creates a dispatcher with a registered contract for the test envelope.
    /// </summary>
    /// <param name="transport">The fake transport recording publications.</param>
    /// <param name="resolveRoute">The route resolver configured on the dispatcher options, or null for none.</param>
    /// <returns>A dispatcher publishing through the fake transport.</returns>
    private static TransportOutboxDispatcher CreateDispatcher(
        TestMessageTransport transport,
        Func<OutboxEnvelope, string>? resolveRoute)
    {
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<TestOrderSubmittedEvent>("orders.events.order-submitted");

        return new TransportOutboxDispatcher(
            transport,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new TransportOutboxDispatcherOptions
            {
                DefaultDestination = "orders.events",
                ResolveRoute = resolveRoute
            });
    }

    /// <summary>
    ///     Sample event used by route resolver tests.
    /// </summary>
    private sealed record TestOrderSubmittedEvent
    {
        /// <summary>
        ///     Gets the order identifier carried by the event payload.
        /// </summary>
        public string OrderId { get; init; } = string.Empty;
    }
}
