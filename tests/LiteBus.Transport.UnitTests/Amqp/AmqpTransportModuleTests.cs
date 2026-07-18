using LiteBus.Transport.Amqp;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies AMQP transport module configuration validation.
/// </summary>
public sealed class AmqpTransportModuleTests
{
    /// <summary>
    ///     Verifies invalid endpoints, recovery delays, and circuit settings fail during module construction.
    /// </summary>
    [Fact]
    public void Constructor_WithUnsafeConnectionOptions_ShouldThrow()
    {
        Action[] actions =
        [
            () => _ = new AmqpTransportModule(new AmqpConnectionOptions { HostName = " " }),
            () => _ = new AmqpTransportModule(new AmqpConnectionOptions { Port = 0 }),
            () => _ = new AmqpTransportModule(new AmqpConnectionOptions
            {
                Uri = new Uri("https://broker.example")
            }),
            () => _ = new AmqpTransportModule(new AmqpConnectionOptions
            {
                NetworkRecoveryInterval = TimeSpan.Zero
            }),
            () => _ = new AmqpTransportModule(new AmqpConnectionOptions
            {
                CircuitBreaker = new AmqpCircuitBreakerOptions { FailureThreshold = -1 }
            }),
            () => _ = new AmqpTransportModule(new AmqpConnectionOptions
            {
                CircuitBreaker = new AmqpCircuitBreakerOptions
                {
                    FailureThreshold = 1,
                    BreakDuration = TimeSpan.Zero
                }
            })
        ];

        foreach (var action in actions)
        {
            action.Should().Throw<ArgumentException>();
        }
    }
}
