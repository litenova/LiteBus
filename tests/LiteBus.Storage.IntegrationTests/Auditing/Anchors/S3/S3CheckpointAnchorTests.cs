using System.Text;
using LiteBus.Auditing.Abstractions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using LiteBus.Auditing.Anchors.S3;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Storage.IntegrationTests.Auditing.Anchors.S3;

/// <summary>
///     Integration tests for the S3 checkpoint anchor: write-once publication, latest checkpoint reads, idempotent republish, and Object Lock retention.
/// </summary>
public sealed class S3CheckpointAnchorTests : IClassFixture<MinioFixture>
{
    private readonly MinioFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="S3CheckpointAnchorTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared MinIO container fixture.</param>
    public S3CheckpointAnchorTests(MinioFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, MinioFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task PublishAsync_then_GetLatestAsync_round_trips_a_checkpoint()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var publisher = CreatePublisher(serviceClient, bucketName);
        var source = CreateSource(serviceClient, bucketName);
        var checkpoint = CreateCheckpoint($"ledger-{Guid.NewGuid():N}/2026", 7, AuditCheckpointKind.Final);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var latest = await source.GetLatestAsync(checkpoint.Segment, CancellationToken.None).ConfigureAwait(false);

        latest.Should().NotBeNull();
        latest!.Id.Should().Be(checkpoint.Id);
        latest.Segment.Should().Be(checkpoint.Segment);
        latest.Sequence.Should().Be(checkpoint.Sequence);
        latest.Count.Should().Be(checkpoint.Count);
        latest.Hash.Should().Be(checkpoint.Hash);
        latest.HashScheme.Should().Be(checkpoint.HashScheme);
        latest.Kind.Should().Be(checkpoint.Kind);
        latest.CreatedAt.Should().Be(checkpoint.CreatedAt);
        latest.Label.Should().Be(checkpoint.Label);
        latest.Signature.Should().Be(checkpoint.Signature);
        latest.Signatures.Should().Equal(checkpoint.Signatures);
    }

    [SkippableFact]
    public async Task PublishAsync_stores_one_write_once_object_as_checkpoint_text()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var publisher = CreatePublisher(serviceClient, bucketName);
        var checkpoint = CreateCheckpoint($"ledger-{Guid.NewGuid():N}/2026", 3);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var keys = await ListKeysAsync(serviceClient, bucketName).ConfigureAwait(false);

        keys.Should().ContainSingle();
        keys[0].Should().Contain($"{checkpoint.Sequence:D20}");
        keys[0].Should().Contain(checkpoint.Id.ToString("N"));
        keys[0].Should().EndWith(".checkpoint");

        var content = await ReadObjectTextAsync(serviceClient, bucketName, keys[0]).ConfigureAwait(false);

        content.Should().StartWith($"{checkpoint.Segment}\n");
        content.Should().Contain("sequence: 3");
        content.Should().Contain("— test ");
    }

    [SkippableFact]
    public async Task PublishAsync_twice_with_the_same_checkpoint_is_idempotent()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var publisher = CreatePublisher(serviceClient, bucketName);
        var source = CreateSource(serviceClient, bucketName);
        var checkpoint = CreateCheckpoint($"ledger-{Guid.NewGuid():N}/2026", 2);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var latest = await source.GetLatestAsync(checkpoint.Segment, CancellationToken.None).ConfigureAwait(false);
        latest.Should().NotBeNull();
        latest!.Id.Should().Be(checkpoint.Id);

        var keys = await ListKeysAsync(serviceClient, bucketName).ConfigureAwait(false);
        keys.Should().ContainSingle();
    }

    [SkippableFact]
    public async Task PublishAsync_then_GetLatestAsync_round_trips_an_unsigned_checkpoint()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var publisher = CreatePublisher(serviceClient, bucketName);
        var source = CreateSource(serviceClient, bucketName);
        var checkpoint = CreateUnsignedCheckpoint($"ledger-{Guid.NewGuid():N}/2026", 1);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var latest = await source.GetLatestAsync(checkpoint.Segment, CancellationToken.None).ConfigureAwait(false);

        latest.Should().NotBeNull();
        latest!.Id.Should().Be(checkpoint.Id);
        latest.Signature.Should().BeNull();
        latest.Signatures.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task GetLatestAsync_without_checkpoints_returns_null()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var source = CreateSource(serviceClient, NewBucketName());

        var latest = await source.GetLatestAsync($"ledger-{Guid.NewGuid():N}/2026", CancellationToken.None).ConfigureAwait(false);

        latest.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetLatestAsync_without_checkpoints_in_an_existing_bucket_returns_null()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var publisher = CreatePublisher(serviceClient, bucketName);
        var source = CreateSource(serviceClient, bucketName);
        var stored = CreateCheckpoint($"stored-{Guid.NewGuid():N}/2026", 1);

        await publisher.PublishAsync(stored, CancellationToken.None).ConfigureAwait(false);

        var latest = await source.GetLatestAsync($"missing-{Guid.NewGuid():N}/2026", CancellationToken.None).ConfigureAwait(false);

        latest.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetLatestAsync_returns_the_highest_sequence()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var publisher = CreatePublisher(serviceClient, bucketName);
        var source = CreateSource(serviceClient, bucketName);
        var segment = $"ledger-{Guid.NewGuid():N}/2026";
        var first = CreateCheckpoint(segment, 1);
        var third = CreateCheckpoint(segment, 3);
        var second = CreateCheckpoint(segment, 2);

        await publisher.PublishAsync(first, CancellationToken.None).ConfigureAwait(false);
        await publisher.PublishAsync(third, CancellationToken.None).ConfigureAwait(false);
        await publisher.PublishAsync(second, CancellationToken.None).ConfigureAwait(false);

        var latest = await source.GetLatestAsync(segment, CancellationToken.None).ConfigureAwait(false);

        latest.Should().NotBeNull();
        latest!.Id.Should().Be(third.Id);
        latest.Sequence.Should().Be(3);
    }

    [SkippableFact]
    public async Task GetLatestAsync_reads_nested_segments_independently()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var publisher = CreatePublisher(serviceClient, bucketName);
        var source = CreateSource(serviceClient, bucketName);
        var tenant = $"tenant-{Guid.NewGuid():N}";
        var parent = CreateCheckpoint(tenant, 9);
        var nested = CreateCheckpoint($"{tenant}/2026", 5);

        await publisher.PublishAsync(parent, CancellationToken.None).ConfigureAwait(false);
        await publisher.PublishAsync(nested, CancellationToken.None).ConfigureAwait(false);

        var parentLatest = await source.GetLatestAsync(tenant, CancellationToken.None).ConfigureAwait(false);
        var nestedLatest = await source.GetLatestAsync(nested.Segment, CancellationToken.None).ConfigureAwait(false);

        parentLatest.Should().NotBeNull();
        parentLatest!.Id.Should().Be(parent.Id);
        parentLatest.Sequence.Should().Be(9);
        nestedLatest.Should().NotBeNull();
        nestedLatest!.Id.Should().Be(nested.Id);
        nestedLatest.Sequence.Should().Be(5);
    }

    [SkippableFact]
    public async Task GetLatestAsync_ignores_foreign_objects_under_the_segment_prefix()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var publisher = CreatePublisher(serviceClient, bucketName);
        var source = CreateSource(serviceClient, bucketName);
        var segment = $"ledger-{Guid.NewGuid():N}/2026";
        var checkpoint = CreateCheckpoint(segment, 4);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        await serviceClient.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = $"{S3CheckpointAnchorKeys.GetSegmentPrefix("", segment)}readme.txt",
                    ContentBody = "foreign file",
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        var latest = await source.GetLatestAsync(segment, CancellationToken.None).ConfigureAwait(false);

        latest.Should().NotBeNull();
        latest!.Id.Should().Be(checkpoint.Id);
    }

    [SkippableFact]
    public async Task GetLatestAsync_with_an_unreadable_checkpoint_object_throws()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var publisher = CreatePublisher(serviceClient, bucketName);
        var source = CreateSource(serviceClient, bucketName);
        var segment = $"ledger-{Guid.NewGuid():N}/2026";
        var stored = CreateCheckpoint(segment, 1);

        await publisher.PublishAsync(stored, CancellationToken.None).ConfigureAwait(false);
        await serviceClient.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = S3CheckpointAnchorKeys.GetObjectKey("", segment, 9, Guid.NewGuid()),
                    ContentBody = "not a checkpoint",
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        var read = async () => await source.GetLatestAsync(segment, CancellationToken.None).ConfigureAwait(false);

        await read.Should().ThrowAsync<S3CheckpointAnchorException>().ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task PublishAsync_with_governance_retention_protects_the_object()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var options = CreateOptions(bucketName) with
        {
            RetentionMode = S3CheckpointRetentionMode.Governance,
            RetentionPeriod = TimeSpan.FromDays(30),
        };
        var publisher = new S3CheckpointPublisher(serviceClient, options);
        var checkpoint = CreateCheckpoint($"ledger-{Guid.NewGuid():N}/2026", 1);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var keys = await ListKeysAsync(serviceClient, bucketName).ConfigureAwait(false);
        keys.Should().ContainSingle();

        var metadata = await serviceClient.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = bucketName,
                    Key = keys[0],
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        metadata.ObjectLockMode.Should().Be(ObjectLockMode.Governance);
        metadata.ObjectLockRetainUntilDate.Should().NotBeNull();
        metadata.ObjectLockRetainUntilDate!.Value.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));

        var remove = async () => await serviceClient.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = bucketName,
                    Key = keys[0],
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        await remove.Should().ThrowAsync<AmazonS3Exception>().ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task PublishAsync_with_compliance_retention_sets_the_mode()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var options = CreateOptions(bucketName) with
        {
            RetentionMode = S3CheckpointRetentionMode.Compliance,
            RetentionPeriod = TimeSpan.FromDays(7),
        };
        var publisher = new S3CheckpointPublisher(serviceClient, options);
        var checkpoint = CreateCheckpoint($"ledger-{Guid.NewGuid():N}/2026", 1);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var keys = await ListKeysAsync(serviceClient, bucketName).ConfigureAwait(false);
        keys.Should().ContainSingle();

        var metadata = await serviceClient.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = bucketName,
                    Key = keys[0],
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        metadata.ObjectLockMode.Should().Be(ObjectLockMode.Compliance);
        metadata.ObjectLockRetainUntilDate.Should().NotBeNull();
        metadata.ObjectLockRetainUntilDate!.Value.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(5));
    }

    [SkippableFact]
    public async Task PublishAsync_without_retention_leaves_the_object_deletable()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var publisher = CreatePublisher(serviceClient, bucketName);
        var checkpoint = CreateCheckpoint($"ledger-{Guid.NewGuid():N}/2026", 1);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var keys = await ListKeysAsync(serviceClient, bucketName).ConfigureAwait(false);
        keys.Should().ContainSingle();

        await serviceClient.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = bucketName,
                    Key = keys[0],
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        var remaining = await ListKeysAsync(serviceClient, bucketName).ConfigureAwait(false);
        remaining.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Diagnostic_check_reports_healthy_when_the_bucket_exists()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var bucketName = NewBucketName();
        var publisher = CreatePublisher(serviceClient, bucketName);
        var options = CreateOptions(bucketName);
        var checkpoint = CreateCheckpoint($"ledger-{Guid.NewGuid():N}/2026", 1);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var check = new S3CheckpointAnchorDiagnosticCheck(serviceClient, options);
        var result = await check.CheckAsync(CancellationToken.None).ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Healthy);
    }

    [SkippableFact]
    public async Task Diagnostic_check_reports_degraded_when_the_bucket_is_missing()
    {
        using var serviceClient = CreateServiceClient(_fixture.SecretKey);
        var options = CreateOptions(NewBucketName());
        var check = new S3CheckpointAnchorDiagnosticCheck(serviceClient, options);

        var result = await check.CheckAsync(CancellationToken.None).ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Degraded);
    }

    [SkippableFact]
    public async Task Diagnostic_check_reports_unhealthy_with_wrong_credentials()
    {
        using var serviceClient = CreateServiceClient("wrong-secret-key");
        var options = CreateOptions(NewBucketName());
        var check = new S3CheckpointAnchorDiagnosticCheck(serviceClient, options);

        var result = await check.CheckAsync(CancellationToken.None).ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Unhealthy);
    }

    private static string NewBucketName()
    {
        return $"checkpoints-{Guid.NewGuid():N}";
    }

    private AmazonS3Client CreateServiceClient(string secretKey)
    {
        return new AmazonS3Client(
            new BasicAWSCredentials(_fixture.AccessKey, secretKey),
            new AmazonS3Config
            {
                ServiceURL = _fixture.ServiceUrl,
                ForcePathStyle = true,
            });
    }

    private S3CheckpointPublisher CreatePublisher(IAmazonS3 serviceClient, string bucketName)
    {
        return new S3CheckpointPublisher(serviceClient, CreateOptions(bucketName));
    }

    private S3CheckpointSource CreateSource(IAmazonS3 serviceClient, string bucketName)
    {
        return new S3CheckpointSource(serviceClient, CreateOptions(bucketName));
    }

    private S3CheckpointAnchorOptions CreateOptions(string bucketName)
    {
        return new S3CheckpointAnchorOptions
        {
            BucketName = bucketName,
            Region = "us-east-1",
            ServiceUrl = _fixture.ServiceUrl,
            AccessKey = _fixture.AccessKey,
            SecretKey = _fixture.SecretKey,
            ForcePathStyle = true,
        };
    }

    private static AuditCheckpoint CreateCheckpoint(
        string segment,
        long sequence,
        AuditCheckpointKind kind = AuditCheckpointKind.Regular)
    {
        return new AuditCheckpoint(
            Guid.NewGuid(),
            segment,
            sequence,
            sequence,
            $"hash-{sequence}",
            "sha256-v2",
            kind,
            new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero).AddTicks(sequence))
        {
            Label = $"label-{sequence}",
            Signature = TestSignature("test", $"signature-bytes-for-test-signer-{sequence}"),
            Signatures =
            [
                TestSignature("test", $"signature-bytes-for-test-signer-{sequence}"),
                TestSignature("witness", $"signature-bytes-for-witness-{sequence}"),
            ],
        };
    }

    private static AuditCheckpoint CreateUnsignedCheckpoint(string segment, long sequence)
    {
        return new AuditCheckpoint(
            Guid.NewGuid(),
            segment,
            sequence,
            sequence,
            $"hash-{sequence}",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero).AddTicks(sequence));
    }

    private static AuditCheckpointSignature TestSignature(string scheme, string text)
    {
        return new AuditCheckpointSignature(scheme, Convert.ToBase64String(Encoding.UTF8.GetBytes(text)));
    }

    private static async Task<List<string>> ListKeysAsync(IAmazonS3 serviceClient, string bucketName)
    {
        List<string> keys = [];
        string? continuationToken = null;

        do
        {
            var listing = await serviceClient.ListObjectsV2Async(
                    new ListObjectsV2Request
                    {
                        BucketName = bucketName,
                        ContinuationToken = continuationToken,
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            foreach (var item in listing.S3Objects)
            {
                keys.Add(item.Key);
            }

            continuationToken = listing.IsTruncated == true ? listing.NextContinuationToken : null;
        }
        while (continuationToken is not null);

        return keys;
    }

    private static async Task<string> ReadObjectTextAsync(IAmazonS3 serviceClient, string bucketName, string key)
    {
        using var response = await serviceClient.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = key,
                },
                CancellationToken.None)
            .ConfigureAwait(false);
        using var reader = new StreamReader(response.ResponseStream, Encoding.UTF8);

        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }
}
