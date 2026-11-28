using System;
using LiteBus.Auditing.Abstractions;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;

namespace LiteBus.Auditing.Anchors.S3;

/// <summary>
///     Publishes audit chain checkpoints to S3-compatible object storage as write-once objects.
/// </summary>
/// <remarks>
///     Each checkpoint is stored under an object key holding its segment, sequence, and identifier, so a stored
///     checkpoint is never confused with another. The stored bytes are deterministic per checkpoint, so publishing
///     the same checkpoint again writes identical bytes to its key. On a bucket with Object Lock, every write also
///     carries the configured retention mode and period.
/// </remarks>
public sealed class S3CheckpointPublisher : ICheckpointPublisher
{
    /// <summary>
    ///     Gets the shared S3 client checkpoints are written with.
    /// </summary>
    private readonly IAmazonS3 _client;

    /// <summary>
    ///     Gets the anchor settings naming the bucket and the object key prefix.
    /// </summary>
    private readonly S3CheckpointAnchorOptions _options;

    /// <summary>
    ///     Gets the clock retention expiries are counted from.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Initializes a new instance of the <see cref="S3CheckpointPublisher" /> class.
    /// </summary>
    /// <param name="client">The shared S3 client checkpoints are written with.</param>
    /// <param name="options">The anchor settings naming the bucket and the object key prefix.</param>
    /// <param name="clock">The clock retention expiries are counted from, or <see langword="null" /> for the system clock.</param>
    public S3CheckpointPublisher(
        IAmazonS3 client,
        S3CheckpointAnchorOptions options,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        S3CheckpointAnchorOptionsValidator.Validate(options);
        _client = client;
        _options = options;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The bucket is created with the first published checkpoint, with Object Lock enabled when retention is
    ///     configured. A storage failure throws and leaves the stored checkpoint pending, so the background retrier
    ///     publishes it again.
    /// </remarks>
    public async ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        await EnsureBucketAsync(cancellationToken).ConfigureAwait(false);

        var objectKey = S3CheckpointAnchorKeys.GetObjectKey(
            _options.KeyPrefix,
            checkpoint.Segment,
            checkpoint.Sequence,
            checkpoint.Id);
        var content = S3CheckpointNoteCodec.Serialize(objectKey, checkpoint);

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            ContentBody = content,
        };

        // The options validator requires a period with a mode, so both are set or both are unset.
        if (_options.RetentionMode is { } mode && _options.RetentionPeriod is { } retention)
        {
            request.ObjectLockMode = ToObjectLockMode(mode);
            request.ObjectLockRetainUntilDate = _clock.GetUtcNow().Add(retention).UtcDateTime;
        }

        await _client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates the checkpoint bucket when it does not exist yet.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous creation.</returns>
    /// <remarks>
    ///     A conflict on creation means another publisher created the bucket concurrently, so creation is skipped and
    ///     the write below verifies the bucket is usable.
    /// </remarks>
    private async ValueTask EnsureBucketAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.HeadBucketAsync(
                    new HeadBucketRequest { BucketName = _options.BucketName },
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // The bucket does not exist yet and is created below.
        }

        var create = new PutBucketRequest { BucketName = _options.BucketName };

        if (_options.RetentionMode.HasValue)
        {
            create.ObjectLockEnabledForBucket = true;
        }

        try
        {
            await _client.PutBucketAsync(create, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // Another publisher created the bucket concurrently.
        }
    }

    /// <summary>
    ///     Maps the configured retention mode to the S3 Object Lock mode.
    /// </summary>
    /// <param name="mode">The configured retention mode.</param>
    /// <returns>The S3 Object Lock mode.</returns>
    private static ObjectLockMode ToObjectLockMode(S3CheckpointRetentionMode mode)
    {
        return mode switch
        {
            S3CheckpointRetentionMode.Governance => ObjectLockMode.Governance,
            S3CheckpointRetentionMode.Compliance => ObjectLockMode.Compliance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "The retention mode is not defined."),
        };
    }
}
