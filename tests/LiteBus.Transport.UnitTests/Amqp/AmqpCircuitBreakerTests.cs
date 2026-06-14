using LiteBus.Transport.Amqp;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Unit tests for <see cref="AmqpCircuitBreaker" /> open state and failure counting.
/// </summary>
public sealed class AmqpCircuitBreakerTests
{
    /// <summary>
    ///     Confirms consecutive failures open the circuit and expose the configured threshold via
    ///     <see cref="AmqpCircuitBreaker.FailureCount" /> while open.
    /// </summary>
    [Fact]
    public void RecordFailure_until_threshold_ShouldOpenCircuitAndExposeFailureCount()
    {
        var breaker = new AmqpCircuitBreaker(new AmqpCircuitBreakerOptions
        {
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromMinutes(1)
        });

        breaker.IsOpen.Should().BeFalse();
        breaker.FailureCount.Should().Be(0);

        breaker.RecordFailure();
        breaker.IsOpen.Should().BeFalse();
        breaker.FailureCount.Should().Be(1);

        breaker.RecordFailure();
        breaker.IsOpen.Should().BeTrue();
        breaker.FailureCount.Should().Be(2);

        var act = () => breaker.ThrowIfOpen();
        act.Should().Throw<AmqpCircuitBreakerOpenException>();
    }

    /// <summary>
    ///     Confirms a successful operation resets failure tracking and closes the circuit.
    /// </summary>
    [Fact]
    public void RecordSuccess_after_failures_ShouldCloseCircuit()
    {
        var breaker = new AmqpCircuitBreaker(new AmqpCircuitBreakerOptions
        {
            FailureThreshold = 1,
            BreakDuration = TimeSpan.FromMinutes(1)
        });

        breaker.RecordFailure();
        breaker.IsOpen.Should().BeTrue();

        breaker.RecordSuccess();
        breaker.IsOpen.Should().BeFalse();
        breaker.FailureCount.Should().Be(0);
        breaker.ThrowIfOpen();
    }
}
