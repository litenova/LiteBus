namespace LiteBus.Inbox.UnitTests.Ingress;

/// <summary>
///     Verifies shared ingress adapter consumer bounds fail at composition time.
/// </summary>
public sealed class IngressAdapterOptionsValidationTests
{
    /// <summary>
    ///     Verifies negative prefetch counts are rejected.
    /// </summary>
    /// <param name="prefetchCount">The invalid prefetch count.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ValidatePrefetchCount_WithNegativeValue_ShouldThrow(int prefetchCount)
    {
        var act = () => IngressAdapterOptionsValidator.ValidatePrefetchCount(prefetchCount);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies zero and positive prefetch counts are accepted.
    /// </summary>
    /// <param name="prefetchCount">The valid prefetch count.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void ValidatePrefetchCount_WithValidValue_ShouldNotThrow(int prefetchCount)
    {
        var act = () => IngressAdapterOptionsValidator.ValidatePrefetchCount(prefetchCount);

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Verifies an unset callback concurrency is accepted.
    /// </summary>
    [Fact]
    public void ValidateMaxConcurrentCalls_WhenUnset_ShouldNotThrow()
    {
        var act = () => IngressAdapterOptionsValidator.ValidateMaxConcurrentCalls(null);

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Verifies callback concurrency below one is rejected.
    /// </summary>
    /// <param name="maxConcurrentCalls">The invalid callback concurrency.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ValidateMaxConcurrentCalls_WithValueBelowOne_ShouldThrow(int maxConcurrentCalls)
    {
        var act = () => IngressAdapterOptionsValidator.ValidateMaxConcurrentCalls(maxConcurrentCalls);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies positive callback concurrency is accepted.
    /// </summary>
    /// <param name="maxConcurrentCalls">The valid callback concurrency.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    public void ValidateMaxConcurrentCalls_WithValidValue_ShouldNotThrow(int maxConcurrentCalls)
    {
        var act = () => IngressAdapterOptionsValidator.ValidateMaxConcurrentCalls(maxConcurrentCalls);

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Verifies receive batch sizes outside one through ten are rejected.
    /// </summary>
    /// <param name="receiveBatchSize">The invalid receive batch size.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void ValidateReceiveBatchSize_WithOutOfRangeValue_ShouldThrow(int receiveBatchSize)
    {
        var act = () => IngressAdapterOptionsValidator.ValidateReceiveBatchSize(receiveBatchSize);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies receive batch sizes from one through ten are accepted.
    /// </summary>
    /// <param name="receiveBatchSize">The valid receive batch size.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void ValidateReceiveBatchSize_WithValidValue_ShouldNotThrow(int receiveBatchSize)
    {
        var act = () => IngressAdapterOptionsValidator.ValidateReceiveBatchSize(receiveBatchSize);

        act.Should().NotThrow();
    }
}
