using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Abstractions.Exceptions;
using LiteBus.Outbox.Dispatch;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Outbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies an empty tenant routing strategy result fails dispatch instead of publishing with an empty route.
/// </summary>
public sealed class R3F8TenantRoutingValidationTests
{
    /// <summary>
    ///     Verifies a whitespace strategy result throws with the envelope identity and publishes nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DispatchAsync_with_empty_strategy_route_should_throw(string? route)
    {
        var transport = new TestMessageTransport();
        var dispatcher = CreateDispatcher(transport, route);
        var messageId = Guid.NewGuid();

        var act = () => dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.order-submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1
        });

        var failure = await act.Should().ThrowAsync<LiteBusDispatchException>().ConfigureAwait(false);
        failure.Which.Message.Should().Contain(messageId.ToString("D"));
        failure.Which.Message.Should().Contain("tenant routing strategy");
        transport.Published.Should().BeEmpty();
    }

    private static TransportOutboxDispatcher CreateDispatcher(TestMessageTransport transport, string? route)
    {
        var registry = new MessageContractRegistry();
        registry.Register<TestEvent>("orders.events.order-submitted");

        return new TransportOutboxDispatcher(
            transport,
            registry,
            new SystemTextJsonMessageSerializer(),
            new TransportOutboxDispatcherOptions { DefaultDestination = "orders.events" },
            null,
            new FixedRouteStrategy(route));
    }

    private sealed record TestEvent;

    private sealed class FixedRouteStrategy : ITenantRoutingStrategy
    {
        private readonly string? _route;

        public FixedRouteStrategy(string? route)
        {
            _route = route;
        }

        public string ResolveRoute(string? tenantId, string contractName, string? topic)
        {
            return _route!;
        }
    }
}
