using LiteBus.Transport.AwsSqs;

namespace LiteBus.Transport.UnitTests.AwsSqs;

/// <summary>
///     Verifies SQS visibility extension while delivery handlers run.
/// </summary>
public sealed class SqsVisibilityHeartbeatTests
{
    /// <summary>
    ///     Verifies a long handler observes periodic visibility extensions and returns its result.
    /// </summary>
    [Fact]
    public async Task RunWithVisibilityExtension_LongHandler_ExtendsVisibilityPeriodically()
    {
        var extensions = 0;

        var result = await SqsVisibilityHeartbeat.RunWithVisibilityExtensionAsync(
            _ =>
            {
                Interlocked.Increment(ref extensions);
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(20),
            TimeProvider.System,
            async handlerToken =>
            {
                await Task.Delay(150, handlerToken).ConfigureAwait(false);
                return 7;
            },
            CancellationToken.None).ConfigureAwait(false);

        result.Should().Be(7);
        extensions.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    ///     Verifies extensions stop once the handler completes.
    /// </summary>
    [Fact]
    public async Task RunWithVisibilityExtension_AfterHandler_StopsExtending()
    {
        var extensions = 0;

        await SqsVisibilityHeartbeat.RunWithVisibilityExtensionAsync(
            _ =>
            {
                Interlocked.Increment(ref extensions);
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(20),
            TimeProvider.System,
            async handlerToken =>
            {
                await Task.Delay(60, handlerToken).ConfigureAwait(false);
                return 0;
            },
            CancellationToken.None).ConfigureAwait(false);

        var extensionsAfterHandler = Volatile.Read(ref extensions);
        extensionsAfterHandler.Should().BeGreaterThanOrEqualTo(1);

        await Task.Delay(80).ConfigureAwait(false);

        Volatile.Read(ref extensions).Should().Be(extensionsAfterHandler);
    }

    /// <summary>
    ///     Verifies a non-positive interval runs the handler without extending.
    /// </summary>
    [Fact]
    public async Task RunWithVisibilityExtension_NonPositiveInterval_RunsHandlerWithoutExtending()
    {
        var extensions = 0;

        var result = await SqsVisibilityHeartbeat.RunWithVisibilityExtensionAsync(
            _ =>
            {
                Interlocked.Increment(ref extensions);
                return Task.CompletedTask;
            },
            TimeSpan.Zero,
            TimeProvider.System,
            _ => Task.FromResult(3),
            CancellationToken.None).ConfigureAwait(false);

        result.Should().Be(3);
        extensions.Should().Be(0);
    }

    /// <summary>
    ///     Verifies a failed extension ends the loop without failing the handler.
    /// </summary>
    [Fact]
    public async Task RunWithVisibilityExtension_FailedExtension_StopsLoopAndCompletesHandler()
    {
        var extensions = 0;

        var result = await SqsVisibilityHeartbeat.RunWithVisibilityExtensionAsync<int>(
            _ =>
            {
                Interlocked.Increment(ref extensions);
                throw new InvalidOperationException("receipt handle expired");
            },
            TimeSpan.FromMilliseconds(20),
            TimeProvider.System,
            async handlerToken =>
            {
                await Task.Delay(150, handlerToken).ConfigureAwait(false);
                return 9;
            },
            CancellationToken.None).ConfigureAwait(false);

        result.Should().Be(9);
        extensions.Should().Be(1);
    }

    /// <summary>
    ///     Verifies the extension interval is half the visibility timeout.
    /// </summary>
    [Fact]
    public void ComputeExtensionInterval_PositiveTimeout_ReturnsHalfTimeout()
    {
        SqsVisibilityHeartbeat.ComputeExtensionInterval(30).Should().Be(TimeSpan.FromSeconds(15));
        SqsVisibilityHeartbeat.ComputeExtensionInterval(1).Should().Be(TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    ///     Verifies a non-positive timeout disables extension.
    /// </summary>
    [Fact]
    public void ComputeExtensionInterval_NonPositiveTimeout_ReturnsZero()
    {
        SqsVisibilityHeartbeat.ComputeExtensionInterval(0).Should().Be(TimeSpan.Zero);
        SqsVisibilityHeartbeat.ComputeExtensionInterval(-5).Should().Be(TimeSpan.Zero);
    }
}
