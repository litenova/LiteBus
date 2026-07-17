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

        breaker.RecordFailure(breaker.AcquirePermit());
        breaker.IsOpen.Should().BeFalse();
        breaker.FailureCount.Should().Be(1);

        breaker.RecordFailure(breaker.AcquirePermit());
        breaker.IsOpen.Should().BeTrue();
        breaker.FailureCount.Should().Be(2);

        var act = () => breaker.AcquirePermit();
        act.Should().Throw<AmqpCircuitBreakerOpenException>();
    }

    /// <summary>
    ///     Confirms a successful operation resets failure tracking and closes the circuit.
    /// </summary>
    [Fact]
    public void RecordSuccess_after_failures_ShouldCloseCircuit()
    {
        var timeProvider = new ManualTimeProvider();
        var breaker = new AmqpCircuitBreaker(
            new AmqpCircuitBreakerOptions
            {
                FailureThreshold = 1,
                BreakDuration = TimeSpan.FromMinutes(1)
            },
            timeProvider);

        breaker.RecordFailure(breaker.AcquirePermit());
        breaker.IsOpen.Should().BeTrue();

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        breaker.RecordSuccess(breaker.AcquirePermit());
        breaker.IsOpen.Should().BeFalse();
        breaker.FailureCount.Should().Be(0);
        breaker.AcquirePermit();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return Volatile.Read(ref _timestamp);
        }

        public void Advance(TimeSpan elapsed)
        {
            Interlocked.Add(ref _timestamp, elapsed.Ticks);
        }
    }
}
