namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Verifies publisher circuit breaker scope and aggregate state.
/// </summary>
public sealed class TransportCircuitBreakerRegistryTests
{
    /// <summary>
    ///     Confirms each destination has stable, independent resilience state.
    /// </summary>
    [Fact]
    public void GetPublisherCircuit_ShouldScopeStateByDestination()
    {
        var registry = new TransportCircuitBreakerRegistry(new TransportCircuitBreakerOptions
        {
            FailureThreshold = 1,
            BreakDuration = TimeSpan.FromMinutes(1)
        });

        var orders = registry.GetPublisherCircuit("orders");
        var sameOrders = registry.GetPublisherCircuit("orders");
        var billing = registry.GetPublisherCircuit("billing");

        orders.Should().BeSameAs(sameOrders);
        billing.Should().NotBeSameAs(orders);

        orders.RecordFailure(orders.AcquirePermit());

        orders.IsOpen.Should().BeTrue();
        billing.IsOpen.Should().BeFalse();
        registry.IsAnyOpen.Should().BeTrue();
        registry.FailureCount.Should().Be(1);
    }
}
