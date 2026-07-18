using Amazon.Runtime;
using Amazon.SQS;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Transport.AwsSqs;

namespace LiteBus.Transport.UnitTests.AwsSqs;

/// <summary>
///     Verifies SQS diagnostic behavior that does not require a live AWS endpoint.
/// </summary>
public sealed class AwsSqsConnectivityDiagnosticCheckTests
{
    /// <summary>
    ///     Verifies an omitted least-privilege queue target reports degraded instead of a false healthy result.
    /// </summary>
    /// <returns>A task that completes when the diagnostic result is available.</returns>
    [Fact]
    public async Task CheckAsync_WithoutQueueUrl_ShouldReturnDegraded()
    {
        using var client = new AmazonSQSClient(
            new AnonymousAWSCredentials(),
            new AmazonSQSConfig { ServiceURL = "http://127.0.0.1:1" });
        var check = new AwsSqsConnectivityDiagnosticCheck(client, new AwsSqsTransportOptions());

        var result = await check.CheckAsync().ConfigureAwait(false);

        check.Name.Should().Be("transport.sqs.connectivity");
        result.Status.Should().Be(DiagnosticStatus.Degraded);
        result.Description.Should().Contain(nameof(AwsSqsTransportOptions.ConnectivityCheckQueueUrl));
    }
}
