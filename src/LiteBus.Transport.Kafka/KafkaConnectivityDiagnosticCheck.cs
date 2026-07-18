using Confluent.Kafka;
using Confluent.Kafka.Admin;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Transport.Kafka;

/// <summary>
    ///     Verifies that the configured Kafka cluster returns its description within a bounded interval.
/// </summary>
public sealed class KafkaConnectivityDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     Gets the admin client used for the cluster description request.
    /// </summary>
    private readonly IAdminClient _adminClient;

    /// <summary>
    ///     Gets the transport settings containing the diagnostic request timeout.
    /// </summary>
    private readonly KafkaTransportOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaConnectivityDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="adminClient">The admin client used for the cluster description request.</param>
    /// <param name="options">The transport settings containing the diagnostic request timeout.</param>
    public KafkaConnectivityDiagnosticCheck(IAdminClient adminClient, KafkaTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(adminClient);
        ArgumentNullException.ThrowIfNull(options);
        _adminClient = adminClient;
        _options = options;
    }

    /// <inheritdoc />
    public string Name => "transport.kafka.connectivity";

    /// <inheritdoc />
    /// <remarks>
    ///     Confluent's cluster description API accepts an explicit request timeout. The final
    ///     <see cref="Exception" /> handler maps unexpected SDK failures without exposing provider details.
    /// </remarks>
    public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _adminClient.DescribeClusterAsync(new DescribeClusterOptions
                {
                    RequestTimeout = _options.ConnectivityCheckTimeout
                })
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return result.Nodes.Count > 0
                ? new DiagnosticResult(
                    DiagnosticStatus.Healthy,
                    "Kafka cluster description is available.",
                    new Dictionary<string, object> { ["brokerCount"] = result.Nodes.Count })
                : CreateUnhealthyResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CreateUnhealthyResult();
        }
        catch (KafkaException)
        {
            return CreateUnhealthyResult();
        }
#pragma warning disable CA1031 // Diagnostic probes must map unexpected failures to unhealthy results without throwing.
        catch (Exception)
#pragma warning restore CA1031
        {
            return CreateUnhealthyResult();
        }
    }

    /// <summary>
    ///     Creates an unhealthy result without exposing SDK exception text.
    /// </summary>
    /// <returns>The unhealthy diagnostic result.</returns>
    private static DiagnosticResult CreateUnhealthyResult()
    {
        return new DiagnosticResult(
            DiagnosticStatus.Unhealthy,
            "Kafka cluster description is unavailable.");
    }
}
