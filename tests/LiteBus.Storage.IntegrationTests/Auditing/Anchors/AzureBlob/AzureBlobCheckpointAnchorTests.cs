using Azure.Storage.Blobs;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Anchors.AzureBlob;

namespace LiteBus.Storage.IntegrationTests.Auditing.Anchors.AzureBlob;

/// <summary>
///     Integration tests for the Azure Blob checkpoint anchor: write-once publication, latest checkpoint reads, and idempotent republish.
/// </summary>
public sealed class AzureBlobCheckpointAnchorTests : IClassFixture<AzuriteFixture>
{
    private readonly AzuriteFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureBlobCheckpointAnchorTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Azurite container fixture.</param>
    public AzureBlobCheckpointAnchorTests(AzuriteFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, AzuriteFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task PublishAsync_then_GetLatestAsync_round_trips_a_checkpoint()
    {
        var serviceClient = new BlobServiceClient(_fixture.ConnectionString);
        var containerName = NewContainerName();
        var publisher = CreatePublisher(serviceClient, containerName);
        var source = CreateSource(serviceClient, containerName);
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
    public async Task PublishAsync_stores_one_write_once_blob()
    {
        var serviceClient = new BlobServiceClient(_fixture.ConnectionString);
        var containerName = NewContainerName();
        var publisher = CreatePublisher(serviceClient, containerName);
        var checkpoint = CreateCheckpoint($"ledger-{Guid.NewGuid():N}/2026", 3);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var names = await ListBlobNamesAsync(serviceClient, containerName).ConfigureAwait(false);

        names.Should().ContainSingle();
        names[0].Should().Contain($"{checkpoint.Sequence:D20}");
        names[0].Should().Contain(checkpoint.Id.ToString("N"));
        names[0].Should().EndWith(".json");
    }

    [SkippableFact]
    public async Task PublishAsync_twice_with_the_same_checkpoint_is_idempotent()
    {
        var serviceClient = new BlobServiceClient(_fixture.ConnectionString);
        var containerName = NewContainerName();
        var publisher = CreatePublisher(serviceClient, containerName);
        var source = CreateSource(serviceClient, containerName);
        var checkpoint = CreateCheckpoint($"ledger-{Guid.NewGuid():N}/2026", 2);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var latest = await source.GetLatestAsync(checkpoint.Segment, CancellationToken.None).ConfigureAwait(false);
        latest.Should().NotBeNull();
        latest!.Id.Should().Be(checkpoint.Id);

        var names = await ListBlobNamesAsync(serviceClient, containerName).ConfigureAwait(false);
        names.Should().ContainSingle();
    }

    [SkippableFact]
    public async Task PublishAsync_with_the_same_id_but_different_content_throws()
    {
        var serviceClient = new BlobServiceClient(_fixture.ConnectionString);
        var containerName = NewContainerName();
        var publisher = CreatePublisher(serviceClient, containerName);
        var checkpoint = CreateCheckpoint($"ledger-{Guid.NewGuid():N}/2026", 4);

        await publisher.PublishAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var conflicting = checkpoint with { Hash = "tampered-hash" };
        var publish = async () => await publisher.PublishAsync(conflicting, CancellationToken.None).ConfigureAwait(false);

        var thrown = await publish.Should().ThrowAsync<AzureBlobCheckpointAnchorException>().ConfigureAwait(false);
        thrown.WithMessage($"*{checkpoint.Id}*");
    }

    [SkippableFact]
    public async Task GetLatestAsync_without_checkpoints_returns_null()
    {
        var serviceClient = new BlobServiceClient(_fixture.ConnectionString);
        var source = CreateSource(serviceClient, NewContainerName());

        var latest = await source.GetLatestAsync($"ledger-{Guid.NewGuid():N}/2026", CancellationToken.None).ConfigureAwait(false);

        latest.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetLatestAsync_without_checkpoints_in_an_existing_container_returns_null()
    {
        var serviceClient = new BlobServiceClient(_fixture.ConnectionString);
        var containerName = NewContainerName();
        var publisher = CreatePublisher(serviceClient, containerName);
        var source = CreateSource(serviceClient, containerName);
        var stored = CreateCheckpoint($"stored-{Guid.NewGuid():N}/2026", 1);

        await publisher.PublishAsync(stored, CancellationToken.None).ConfigureAwait(false);

        var latest = await source.GetLatestAsync($"missing-{Guid.NewGuid():N}/2026", CancellationToken.None).ConfigureAwait(false);

        latest.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetLatestAsync_returns_the_highest_sequence()
    {
        var serviceClient = new BlobServiceClient(_fixture.ConnectionString);
        var containerName = NewContainerName();
        var publisher = CreatePublisher(serviceClient, containerName);
        var source = CreateSource(serviceClient, containerName);
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
        var serviceClient = new BlobServiceClient(_fixture.ConnectionString);
        var containerName = NewContainerName();
        var publisher = CreatePublisher(serviceClient, containerName);
        var source = CreateSource(serviceClient, containerName);
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

    private static string NewContainerName()
    {
        return $"checkpoints-{Guid.NewGuid():N}";
    }

    private AzureBlobCheckpointPublisher CreatePublisher(BlobServiceClient serviceClient, string containerName)
    {
        return new AzureBlobCheckpointPublisher(serviceClient, CreateOptions(containerName));
    }

    private AzureBlobCheckpointSource CreateSource(BlobServiceClient serviceClient, string containerName)
    {
        return new AzureBlobCheckpointSource(serviceClient, CreateOptions(containerName));
    }

    private AzureBlobCheckpointAnchorOptions CreateOptions(string containerName)
    {
        return new AzureBlobCheckpointAnchorOptions
        {
            ConnectionString = _fixture.ConnectionString,
            ContainerName = containerName,
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
            Signature = new AuditCheckpointSignature("test", $"sig-{sequence}"),
            Signatures =
            [
                new AuditCheckpointSignature("test", $"sig-{sequence}"),
                new AuditCheckpointSignature("witness", $"wit-{sequence}"),
            ],
        };
    }

    private static async Task<List<string>> ListBlobNamesAsync(BlobServiceClient serviceClient, string containerName)
    {
        var container = serviceClient.GetBlobContainerClient(containerName);
        List<string> names = [];

        await foreach (var item in container.GetBlobsAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false))
        {
            names.Add(item.Name);
        }

        return names;
    }
}
