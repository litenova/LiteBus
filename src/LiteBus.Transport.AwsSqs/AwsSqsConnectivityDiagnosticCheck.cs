using System.Net;
using Amazon.SQS;
using Amazon.SQS.Model;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Transport.AwsSqs;

/// <summary>
///     Verifies that the configured SQS client can read one target queue using least-privilege queue permissions.
/// </summary>
public sealed class AwsSqsConnectivityDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     Gets the SQS client used to read queue attributes.
    /// </summary>
    private readonly IAmazonSQS _client;

    /// <summary>
    ///     Gets the transport settings containing the diagnostic queue URL.
    /// </summary>
    private readonly AwsSqsTransportOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsConnectivityDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="client">The SQS client used to read queue attributes.</param>
    /// <param name="options">The transport settings containing the diagnostic queue URL.</param>
    public AwsSqsConnectivityDiagnosticCheck(IAmazonSQS client, AwsSqsTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options;
    }

    /// <inheritdoc />
    public string Name => "transport.sqs.connectivity";

    /// <inheritdoc />
    /// <remarks>
    ///     The final <see cref="Exception" /> handler prevents an adapter diagnostic from escaping into sibling probes.
    /// </remarks>
    public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectivityCheckQueueUrl))
        {
            return new DiagnosticResult(
                DiagnosticStatus.Degraded,
                "SQS connectivity is not configured; set ConnectivityCheckQueueUrl.");
        }

        try
        {
            var response = await _client.GetQueueAttributesAsync(
                    new GetQueueAttributesRequest
                    {
                        QueueUrl = _options.ConnectivityCheckQueueUrl,
                        AttributeNames = [QueueAttributeName.QueueArn]
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return response.HttpStatusCode == HttpStatusCode.OK && !string.IsNullOrWhiteSpace(response.QueueARN)
                ? new DiagnosticResult(DiagnosticStatus.Healthy, "SQS queue attributes are available.")
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
        catch (AmazonSQSException)
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
            "SQS queue attributes are unavailable.");
    }
}
