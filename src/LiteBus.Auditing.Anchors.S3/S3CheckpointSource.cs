using System;
using LiteBus.Auditing.Abstractions;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Anchors.S3;

/// <summary>
///     Reads the latest checkpoint of a segment back from S3-compatible object storage.
/// </summary>
public sealed class S3CheckpointSource : ICheckpointSource
{
    /// <summary>
    ///     Gets the shared S3 client checkpoints are read with.
    /// </summary>
    private readonly IAmazonS3 _client;

    /// <summary>
    ///     Gets the anchor settings naming the bucket and the object key prefix.
    /// </summary>
    private readonly S3CheckpointAnchorOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="S3CheckpointSource" /> class.
    /// </summary>
    /// <param name="client">The shared S3 client checkpoints are read with.</param>
    /// <param name="options">The anchor settings naming the bucket and the object key prefix.</param>
    public S3CheckpointSource(IAmazonS3 client, S3CheckpointAnchorOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        S3CheckpointAnchorOptionsValidator.Validate(options);
        _client = client;
        _options = options;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A missing bucket or a segment without checkpoint objects reads as <see langword="null" />. Every
    ///     checkpoint object under the segment is parsed, and an unreadable one throws
    ///     <see cref="S3CheckpointAnchorException" /> instead of being skipped, so a damaged anchor cannot
    ///     pass as the latest checkpoint. Storage I/O failures surface as the AWS SDK exception.
    /// </remarks>
    public async ValueTask<AuditCheckpoint?> GetLatestAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        if (!await BucketExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var prefix = S3CheckpointAnchorKeys.GetSegmentPrefix(_options.KeyPrefix, segment);

        AuditCheckpoint? latest = null;
        string? latestKey = null;
        string? continuationToken = null;

        do
        {
            var listing = await _client.ListObjectsV2Async(
                    new ListObjectsV2Request
                    {
                        BucketName = _options.BucketName,
                        Prefix = prefix,
                        MaxKeys = 1000,
                        ContinuationToken = continuationToken,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var item in listing.S3Objects)
            {
                if (!S3CheckpointAnchorKeys.IsCheckpointObject(item.Key))
                {
                    continue;
                }

                var checkpoint = await ReadCheckpointAsync(item.Key, cancellationToken).ConfigureAwait(false);

                if (latest is null ||
                    checkpoint.Sequence > latest.Sequence ||
                    (checkpoint.Sequence == latest.Sequence &&
                        string.Compare(item.Key, latestKey, StringComparison.Ordinal) > 0))
                {
                    latest = checkpoint;
                    latestKey = item.Key;
                }
            }

            continuationToken = listing.IsTruncated == true ? listing.NextContinuationToken : null;
        }
        while (continuationToken is not null);

        return latest;
    }

    /// <summary>
    ///     Determines whether the checkpoint bucket exists.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns><see langword="true" /> when the bucket exists; otherwise, <see langword="false" />.</returns>
    private async ValueTask<bool> BucketExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.HeadBucketAsync(
                    new HeadBucketRequest { BucketName = _options.BucketName },
                    cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    ///     Reads and parses one checkpoint object.
    /// </summary>
    /// <param name="objectKey">The checkpoint object key.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The checkpoint the object holds.</returns>
    private async ValueTask<AuditCheckpoint> ReadCheckpointAsync(string objectKey, CancellationToken cancellationToken)
    {
        using var response = await _client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = objectKey,
                },
                cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(response.ResponseStream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        return S3CheckpointNoteCodec.Deserialize(objectKey, content);
    }
}
