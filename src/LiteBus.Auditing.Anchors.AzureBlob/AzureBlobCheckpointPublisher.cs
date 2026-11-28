using System;
using LiteBus.Auditing.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace LiteBus.Auditing.Anchors.AzureBlob;

/// <summary>
///     Publishes audit chain checkpoints to Azure Blob Storage as write-once blobs.
/// </summary>
/// <remarks>
///     Each checkpoint is stored under a blob name holding its segment, sequence, and identifier, so a stored blob is
///     never overwritten. Publishing the same checkpoint again succeeds without writing, while a different checkpoint
///     under an already stored identifier throws <see cref="AzureBlobCheckpointAnchorException" />.
/// </remarks>
public sealed class AzureBlobCheckpointPublisher : ICheckpointPublisher
{
    /// <summary>
    ///     Gets the shared blob service client checkpoints are written with.
    /// </summary>
    private readonly BlobServiceClient _client;

    /// <summary>
    ///     Gets the anchor settings naming the container and the blob name prefix.
    /// </summary>
    private readonly AzureBlobCheckpointAnchorOptions _options;

    /// <summary>
    ///     Gets the clock immutability policy expiries are counted from.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureBlobCheckpointPublisher" /> class.
    /// </summary>
    /// <param name="client">The shared blob service client checkpoints are written with.</param>
    /// <param name="options">The anchor settings naming the container and the blob name prefix.</param>
    /// <param name="clock">The clock immutability policy expiries are counted from, or <see langword="null" /> for the system clock.</param>
    public AzureBlobCheckpointPublisher(
        BlobServiceClient client,
        AzureBlobCheckpointAnchorOptions options,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        AzureBlobCheckpointAnchorOptionsValidator.Validate(options);
        _client = client;
        _options = options;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The container is created with the first published checkpoint. A storage failure throws and leaves the
    ///     stored checkpoint pending, so the background retrier publishes it again.
    /// </remarks>
    public async ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var container = _client.GetBlobContainerClient(_options.ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var blobName = AzureBlobCheckpointBlobNames.GetBlobName(
            _options.BlobNamePrefix,
            checkpoint.Segment,
            checkpoint.Sequence,
            checkpoint.Id);
        var blob = container.GetBlobClient(blobName);
        var content = AzureBlobCheckpointCodec.Serialize(checkpoint);

        try
        {
            await blob.UploadAsync(BinaryData.FromString(content), false, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            await EnsureRepublishMatchesAsync(blob, blobName, content, checkpoint.Id, cancellationToken).ConfigureAwait(false);
        }

        await ApplyRetentionAsync(blob, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Confirms a republish matches the checkpoint already stored under its identifier.
    /// </summary>
    /// <param name="blob">The anchor blob already holding a checkpoint.</param>
    /// <param name="blobName">The anchor blob name, named when the stored content differs.</param>
    /// <param name="content">The republished anchor blob text.</param>
    /// <param name="checkpointId">The republished checkpoint identifier, named when the stored content differs.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>A task representing the asynchronous comparison.</returns>
    /// <exception cref="AzureBlobCheckpointAnchorException">
    ///     Thrown when the stored blob holds a different checkpoint than the republished one.
    /// </exception>
    private static async ValueTask EnsureRepublishMatchesAsync(
        BlobClient blob,
        string blobName,
        string content,
        Guid checkpointId,
        CancellationToken cancellationToken)
    {
        var stored = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);

        if (!string.Equals(stored.Value.Content.ToString(), content, StringComparison.Ordinal))
        {
            throw new AzureBlobCheckpointAnchorException(
                $"The checkpoint '{checkpointId}' cannot be published because the blob '{blobName}' already " +
                "holds a different checkpoint.");
        }
    }

    /// <summary>
    ///     Applies the configured immutability policy and legal hold to a published anchor blob.
    /// </summary>
    /// <param name="blob">The published anchor blob.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous protection.</returns>
    private async ValueTask ApplyRetentionAsync(BlobClient blob, CancellationToken cancellationToken)
    {
        if (_options.ImmutabilityRetention is { } retention)
        {
            var policy = new BlobImmutabilityPolicy
            {
                ExpiresOn = _clock.GetUtcNow().Add(retention),
                PolicyMode = BlobImmutabilityPolicyMode.Unlocked,
            };

            await blob.SetImmutabilityPolicyAsync(policy, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (_options.LegalHold)
        {
            await blob.SetLegalHoldAsync(true, cancellationToken).ConfigureAwait(false);
        }
    }
}
