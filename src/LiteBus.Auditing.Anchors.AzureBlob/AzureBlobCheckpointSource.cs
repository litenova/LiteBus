using System;
using LiteBus.Auditing.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Anchors.AzureBlob;

/// <summary>
///     Reads the latest checkpoint of a segment back from Azure Blob Storage.
/// </summary>
public sealed class AzureBlobCheckpointSource : ICheckpointSource
{
    /// <summary>
    ///     Gets the shared blob service client checkpoints are read with.
    /// </summary>
    private readonly BlobServiceClient _client;

    /// <summary>
    ///     Gets the anchor settings naming the container and the blob name prefix.
    /// </summary>
    private readonly AzureBlobCheckpointAnchorOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureBlobCheckpointSource" /> class.
    /// </summary>
    /// <param name="client">The shared blob service client checkpoints are read with.</param>
    /// <param name="options">The anchor settings naming the container and the blob name prefix.</param>
    public AzureBlobCheckpointSource(BlobServiceClient client, AzureBlobCheckpointAnchorOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        AzureBlobCheckpointAnchorOptionsValidator.Validate(options);
        _client = client;
        _options = options;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A missing container or a segment without checkpoint blobs reads as <see langword="null" />. Every
    ///     checkpoint blob under the segment is parsed, and an unreadable one throws
    ///     <see cref="AzureBlobCheckpointAnchorException" /> instead of being skipped, so a damaged anchor cannot
    ///     pass as the latest checkpoint. Storage I/O failures surface as the Azure SDK exception.
    /// </remarks>
    public async ValueTask<AuditCheckpoint?> GetLatestAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        var container = _client.GetBlobContainerClient(_options.ContainerName);
        var exists = await container.ExistsAsync(cancellationToken).ConfigureAwait(false);

        if (!exists.Value)
        {
            return null;
        }

        var listing = new GetBlobsOptions
        {
            Prefix = AzureBlobCheckpointBlobNames.GetSegmentPrefix(_options.BlobNamePrefix, segment),
        };

        AuditCheckpoint? latest = null;
        string? latestName = null;

        await foreach (var item in container.GetBlobsAsync(listing, cancellationToken).ConfigureAwait(false))
        {
            if (!AzureBlobCheckpointBlobNames.IsCheckpointBlob(item.Name))
            {
                continue;
            }

            var blob = container.GetBlobClient(item.Name);
            var download = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            var checkpoint = AzureBlobCheckpointCodec.Deserialize(item.Name, download.Value.Content.ToString());

            if (latest is null ||
                checkpoint.Sequence > latest.Sequence ||
                (checkpoint.Sequence == latest.Sequence &&
                    string.Compare(item.Name, latestName, StringComparison.Ordinal) > 0))
            {
                latest = checkpoint;
                latestName = item.Name;
            }
        }

        return latest;
    }
}
