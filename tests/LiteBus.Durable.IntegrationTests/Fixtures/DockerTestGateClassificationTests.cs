using Npgsql;

namespace LiteBus.Durable.IntegrationTests.Fixtures;

/// <summary>
///     Verifies <see cref="DockerTestGate" /> classifies database failures by exception type, not by message text.
/// </summary>
public sealed class DockerTestGateClassificationTests
{
    [Fact]
    public void IsDockerUnavailable_WithNpgsqlPipeFailure_ShouldReturnFalse()
    {
        var exception = new NpgsqlException("Exception while reading from stream: broken pipe");

        DockerTestGate.IsDockerUnavailable(exception).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WithNpgsqlPipeFailure_ShouldPropagateOriginal()
    {
        var original = new NpgsqlException("Exception while reading from stream: broken pipe");

        var thrown = await Assert.ThrowsAsync<NpgsqlException>(
            () => DockerTestGate.RunAsync(() => Task.FromException(original))).ConfigureAwait(false);

        thrown.Should().BeSameAs(original);
    }
}
