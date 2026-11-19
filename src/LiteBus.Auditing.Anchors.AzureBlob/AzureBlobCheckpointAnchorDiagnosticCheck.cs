using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Auditing.Anchors.AzureBlob;

/// <summary>
///     Verifies Azure Blob Storage anchor reachability by reading the checkpoint container.
/// </summary>
public sealed class AzureBlobCheckpointAnchorDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     Gets the stable probe name reported to operators and health hosts.
    /// </summary>
    public const string ProbeName = "auditing.anchor.azure_blob.connectivity";

    /// <summary>
    ///     Gets the shared blob service client used to read the checkpoint container.
    /// </summary>
    private readonly BlobServiceClient _client;

    /// <summary>
    ///     Gets the anchor settings naming the checkpoint container.
    /// </summary>
    private readonly AzureBlobCheckpointAnchorOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureBlobCheckpointAnchorDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="client">The shared blob service client used to read the checkpoint container.</param>
    /// <param name="options">The anchor settings naming the checkpoint container.</param>
    public AzureBlobCheckpointAnchorDiagnosticCheck(
        BlobServiceClient client,
        AzureBlobCheckpointAnchorOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options;
    }

    /// <inheritdoc />
    public string Name => ProbeName;

    /// <inheritdoc />
    /// <remarks>
    ///     Reading the container changes nothing. A container that does not exist yet is degraded rather than
    ///     unhealthy, because the publisher creates it with the first checkpoint. The final
    ///     <see cref="Exception" /> handler prevents an adapter diagnostic from escaping into sibling probes.
    /// </remarks>
    public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var container = _client.GetBlobContainerClient(_options.ContainerName);
            var exists = await container.ExistsAsync(cancellationToken).ConfigureAwait(false);

            return exists.Value
                ? new DiagnosticResult(
                    DiagnosticStatus.Healthy,
                    "The Azure Blob checkpoint container is available.")
                : new DiagnosticResult(
                    DiagnosticStatus.Degraded,
                    "The Azure Blob checkpoint container does not exist yet; it is created with the first published checkpoint.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CreateUnhealthyResult();
        }
        catch (RequestFailedException)
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
            "The Azure Blob checkpoint container is unavailable.");
    }
}
