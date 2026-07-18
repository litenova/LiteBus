using Confluent.Kafka;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Transport.Kafka;

namespace LiteBus.Transport.UnitTests.Kafka;

/// <summary>
///     Verifies Kafka connectivity diagnostics without requiring a live cluster.
/// </summary>
public sealed class KafkaConnectivityDiagnosticCheckTests
{
    /// <summary>
    ///     Verifies an unavailable bootstrap endpoint reports unhealthy within the configured request timeout.
    /// </summary>
    /// <returns>A task that completes when the diagnostic result is available.</returns>
    [Fact]
    public async Task CheckAsync_WhenClusterIsUnavailable_ShouldReturnUnhealthy()
    {
        using var adminClient = CreateAdminClient();
        var check = new KafkaConnectivityDiagnosticCheck(
            adminClient,
            new KafkaTransportOptions
            {
                BootstrapServers = "127.0.0.1:1",
                ConnectivityCheckTimeout = TimeSpan.FromMilliseconds(50)
            });

        var result = await check.CheckAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

        check.Name.Should().Be("transport.kafka.connectivity");
        result.Status.Should().Be(DiagnosticStatus.Unhealthy);
        result.Description.Should().Be("Kafka cluster description is unavailable.");
    }

    /// <summary>
    ///     Verifies caller cancellation escapes before a Kafka request starts.
    /// </summary>
    /// <returns>A task that completes when cancellation is observed.</returns>
    [Fact]
    public async Task CheckAsync_WhenCallerCancels_ShouldPropagateCancellation()
    {
        using var adminClient = CreateAdminClient();
        var check = new KafkaConnectivityDiagnosticCheck(
            adminClient,
            new KafkaTransportOptions { BootstrapServers = "127.0.0.1:1" });
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        var act = () => check.CheckAsync(cancellationSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates an admin client that targets a closed local port.
    /// </summary>
    /// <returns>The admin client used by one test.</returns>
    private static IAdminClient CreateAdminClient()
    {
        return new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = "127.0.0.1:1",
            SocketTimeoutMs = 100
        }).Build();
    }
}
