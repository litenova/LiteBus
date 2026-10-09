using LiteBus.Transport.IntegrationTesting;

namespace LiteBus.Transport.IntegrationTests;

/// <summary>
///     Verifies <see cref="DockerTestGate" /> maps container-runtime failures to skips
///     without reclassifying broker-side failures by message text.
/// </summary>
public sealed class DockerTestGateTests
{
    /// <summary>
    ///     Verifies broker-side failures whose messages mention Docker words propagate unwrapped.
    /// </summary>
    /// <param name="message">The broker-side failure message.</param>
    [Theory]
    [InlineData("broken pipe while publishing to the broker")]
    [InlineData("Docker daemon refused the broker connection")]
    [InlineData("container image pull failed for the broker emulator")]
    public async Task RunAsync_WithNonDockerFailureMentioningDockerWords_ShouldPropagateOriginal(string message)
    {
        var original = new InvalidOperationException(message);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DockerTestGate.RunAsync(() => Task.FromException(original))).ConfigureAwait(false);

        thrown.Should().BeSameAs(original);
    }

    /// <summary>
    ///     Verifies successful broker initialization completes without throwing.
    /// </summary>
    [Fact]
    public async Task RunAsync_WhenInitializeSucceeds_ShouldCompleteWithoutThrowing()
    {
        var completed = false;

        await DockerTestGate.RunAsync(() =>
        {
            completed = true;
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        completed.Should().BeTrue();
    }
}
