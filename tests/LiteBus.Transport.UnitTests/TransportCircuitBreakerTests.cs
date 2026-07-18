namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Verifies transport circuit breaker state transitions and publisher scope isolation.
/// </summary>
public sealed class TransportCircuitBreakerTests
{
    /// <summary>
    ///     Verifies one recovery probe is admitted after the break duration while sibling probes remain rejected.
    /// </summary>
    [Fact]
    public void AcquirePermit_AfterBreakDuration_ShouldAdmitOneHalfOpenProbe()
    {
        var timeProvider = new ManualTimeProvider();
        var circuitBreaker = CreateCircuitBreaker(timeProvider);
        circuitBreaker.RecordFailure(circuitBreaker.AcquirePermit());

        var blockedAct = circuitBreaker.AcquirePermit;
        blockedAct.Should().Throw<TransportCircuitBreakerOpenException>();

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var admitted = 0;
        var rejected = 0;
        var recoveryPermit = default(TransportCircuitBreakerPermit);

        Parallel.For(
            0,
            32,
            _ =>
            {
                try
                {
                    recoveryPermit = circuitBreaker.AcquirePermit();
                    Interlocked.Increment(ref admitted);
                }
                catch (TransportCircuitBreakerOpenException)
                {
                    Interlocked.Increment(ref rejected);
                }
            });

        admitted.Should().Be(1);
        rejected.Should().Be(31);
        circuitBreaker.IsOpen.Should().BeTrue();

        circuitBreaker.RecordSuccess(recoveryPermit);

        circuitBreaker.IsOpen.Should().BeFalse();
        circuitBreaker.FailureCount.Should().Be(0);
        circuitBreaker.AcquirePermit();
    }

    /// <summary>
    ///     Verifies a failed half-open probe starts a new break duration.
    /// </summary>
    [Fact]
    public void RecordFailure_DuringHalfOpenProbe_ShouldReopenCircuit()
    {
        var timeProvider = new ManualTimeProvider();
        var circuitBreaker = CreateCircuitBreaker(timeProvider);
        circuitBreaker.RecordFailure(circuitBreaker.AcquirePermit());
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var recoveryPermit = circuitBreaker.AcquirePermit();

        circuitBreaker.RecordFailure(recoveryPermit);
        timeProvider.Advance(TimeSpan.FromSeconds(29));

        var blockedAct = circuitBreaker.AcquirePermit;
        blockedAct.Should().Throw<TransportCircuitBreakerOpenException>();

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        circuitBreaker.AcquirePermit();
    }

    /// <summary>
    ///     Verifies an inconclusive half-open probe releases its exclusive slot without reporting broker recovery.
    /// </summary>
    [Fact]
    public void ReleasePermit_DuringHalfOpenProbe_ShouldAllowAnotherProbe()
    {
        var timeProvider = new ManualTimeProvider();
        var circuitBreaker = CreateCircuitBreaker(timeProvider);
        circuitBreaker.RecordFailure(circuitBreaker.AcquirePermit());
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        var abandonedProbe = circuitBreaker.AcquirePermit();

        circuitBreaker.ReleasePermit(abandonedProbe);

        circuitBreaker.IsOpen.Should().BeTrue();
        var replacementProbe = circuitBreaker.AcquirePermit();
        circuitBreaker.RecordSuccess(replacementProbe);
        circuitBreaker.IsOpen.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies failures reported while a circuit is already open do not extend its deadline.
    /// </summary>
    [Fact]
    public void RecordFailure_WhileOpen_ShouldNotExtendBreakDuration()
    {
        var timeProvider = new ManualTimeProvider();
        var circuitBreaker = CreateCircuitBreaker(timeProvider);
        var openingPermit = circuitBreaker.AcquirePermit();
        var stalePermit = circuitBreaker.AcquirePermit();
        circuitBreaker.RecordFailure(openingPermit);
        timeProvider.Advance(TimeSpan.FromSeconds(29));

        circuitBreaker.RecordFailure(stalePermit);
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        circuitBreaker.AcquirePermit();
    }

    /// <summary>
    ///     Verifies a stale successful completion cannot close a circuit opened by a sibling operation.
    /// </summary>
    [Fact]
    public void RecordSuccess_FromPreviousGeneration_ShouldNotCloseCircuit()
    {
        var timeProvider = new ManualTimeProvider();
        var circuitBreaker = CreateCircuitBreaker(timeProvider);
        var openingPermit = circuitBreaker.AcquirePermit();
        var stalePermit = circuitBreaker.AcquirePermit();

        circuitBreaker.RecordFailure(openingPermit);
        circuitBreaker.RecordSuccess(stalePermit);

        circuitBreaker.IsOpen.Should().BeTrue();
        var blockedAct = circuitBreaker.AcquirePermit;
        blockedAct.Should().Throw<TransportCircuitBreakerOpenException>();
    }

    private static TransportCircuitBreaker CreateCircuitBreaker(TimeProvider timeProvider)
    {
        return new TransportCircuitBreaker(new TransportCircuitBreakerOptions
        {
            FailureThreshold = 1,
            BreakDuration = TimeSpan.FromSeconds(30)
        }, timeProvider);
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
