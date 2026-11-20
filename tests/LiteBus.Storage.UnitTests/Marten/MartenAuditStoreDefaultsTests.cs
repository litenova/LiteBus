using LiteBus.Auditing.Storage.Marten;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Recording;
using LiteBus.Auditing.Hashing;

namespace LiteBus.Storage.UnitTests.Marten;

/// <summary>
///     Tests for the Marten audit store defaults: the record mapper, segmenters, canonicalizer, schemes, and
///     document mappings.
/// </summary>
public sealed class MartenAuditStoreDefaultsTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Mapper_flattens_the_record_and_keeps_the_record_identity()
    {
        var record = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = "orders.place-order",
            Actor = AuditActor.User("account-7"),
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = OccurredAt,
            Duration = TimeSpan.FromMilliseconds(12),
            Category = "orders",
            TargetKind = "order",
            TargetId = "order-9",
            Reason = "customer request",
            MessageType = "PlaceOrder",
            CorrelationId = "correlation-1",
            TenantId = "tenant-a",
            Properties = new Dictionary<string, string> { ["order-total"] = "9.50" }
        };

        var document = new MartenAuditRecordMapper().Map(record);

        document.Id.Should().Be(record.Id);
        document.Action.Should().Be("orders.place-order");
        document.ActorId.Should().Be("account-7");
        document.ActorKind.Should().Be(AuditActor.UserKind);
        document.Outcome.Should().Be(AuditOutcome.Succeeded);
        document.OccurredAt.Should().Be(OccurredAt);
        document.Duration.Should().Be(TimeSpan.FromMilliseconds(12));
        document.Category.Should().Be("orders");
        document.TargetKind.Should().Be("order");
        document.TargetId.Should().Be("order-9");
        document.Reason.Should().Be("customer request");
        document.MessageType.Should().Be("PlaceOrder");
        document.CorrelationId.Should().Be("correlation-1");
        document.TenantId.Should().Be("tenant-a");
        document.Properties.Should().Contain("order-total", "9.50");
        document.Sequence.Should().Be(0);
        document.Hash.Should().BeNull();
    }

    [Fact]
    public void Mapper_rejects_a_null_record()
    {
        var map = () => new MartenAuditRecordMapper().Map(null!);

        map.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Segmenter_groups_records_by_category_and_year()
    {
        var document = new MartenAuditRecord { Category = "orders", OccurredAt = OccurredAt };

        new MartenAuditRecordSegmenter().SegmentOf(document).Should().Be("orders/2026");
    }

    [Fact]
    public void Segmenter_reports_uncategorized_without_a_category()
    {
        var document = new MartenAuditRecord { OccurredAt = OccurredAt };

        new MartenAuditRecordSegmenter().SegmentOf(document).Should().Be("uncategorized/2026");
    }

    [Fact]
    public void Segmenter_rejects_a_null_document()
    {
        var segment = () => new MartenAuditRecordSegmenter().SegmentOf(null!);

        segment.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Segmenter_opens_segments_without_a_genesis_document()
    {
        var document = new MartenAuditRecord { Category = "orders", OccurredAt = OccurredAt };

        new MartenAuditRecordSegmenter().CreateGenesis("orders/2026", document).Should().BeNull();
    }

    [Fact]
    public void Segmenter_describes_the_tenant_attributes_and_opening()
    {
        var document = new MartenAuditRecord
        {
            Category = "orders",
            OccurredAt = OccurredAt,
            TenantId = "tenant-a"
        };

        var info = new MartenAuditRecordSegmenter().DescribeSegment("orders/2026", document);

        info.Key.Should().Be("orders/2026");
        info.TenantId.Should().Be("tenant-a");
        info.Attributes.Should().BeEquivalentTo(
            new Dictionary<string, string>
            {
                ["category"] = "orders",
                ["year"] = "2026"
            });
        info.OpenedAt.Should().Be(OccurredAt);
    }

    [Fact]
    public void Func_segmenter_delegates_and_opens_without_a_genesis_document()
    {
        var segmenter = new MartenAuditFuncSegmenter<MartenAuditRecord>(document => $"tenant/{document.TenantId}");
        var document = new MartenAuditRecord { TenantId = "tenant-a" };

        segmenter.SegmentOf(document).Should().Be("tenant/tenant-a");
        segmenter.CreateGenesis("tenant/tenant-a", document).Should().BeNull();
    }

    [Fact]
    public void Func_segmenter_rejects_a_null_delegate_and_a_null_document()
    {
        var create = () => new MartenAuditFuncSegmenter<MartenAuditRecord>((Func<MartenAuditRecord, string>) null!);
        create.Should().Throw<ArgumentNullException>();

        var segmenter = new MartenAuditFuncSegmenter<MartenAuditRecord>(document => $"tenant/{document.TenantId}");
        var segment = () => segmenter.SegmentOf(null!);
        segment.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Canonicalizer_is_deterministic_and_binds_the_chain_position()
    {
        var canonicalizer = new MartenAuditRecordCanonicalizer();

        canonicalizer.Canonicalize(ChainedDocument()).Should().Equal(canonicalizer.Canonicalize(ChainedDocument()));

        var baseline = Convert.ToHexString(canonicalizer.Canonicalize(ChainedDocument()));

        var moved = ChainedDocument();
        moved.Sequence += 1;
        Convert.ToHexString(canonicalizer.Canonicalize(moved)).Should().NotBe(baseline);

        var relinked = ChainedDocument();
        relinked.PreviousHash = "another-predecessor-hash";
        Convert.ToHexString(canonicalizer.Canonicalize(relinked)).Should().NotBe(baseline);

        var resegmented = ChainedDocument();
        resegmented.Segment = "ledger/2026";
        Convert.ToHexString(canonicalizer.Canonicalize(resegmented)).Should().NotBe(baseline);
    }

    [Fact]
    public void Canonicalizer_excludes_the_stored_hash_and_orders_properties()
    {
        var canonicalizer = new MartenAuditRecordCanonicalizer();
        var hashed = ChainedDocument();
        hashed.Hash = "whatever-is-stored";
        canonicalizer.Canonicalize(hashed).Should().Equal(canonicalizer.Canonicalize(ChainedDocument()));

        var reordered = ChainedDocument();
        reordered.Properties.Clear();
        reordered.Properties["warehouse"] = "amsterdam";
        reordered.Properties["order-total"] = "9.50";
        canonicalizer.Canonicalize(reordered).Should().Equal(canonicalizer.Canonicalize(ChainedDocument()));
    }

    [Fact]
    public void Schemes_expose_the_default_sha256_scheme()
    {
        MartenAuditHashSchemes.Sha256V1.Name.Should().Be("sha256-v1");
        MartenAuditHashSchemes.Sha256V1.Canonicalizer.Should().BeOfType<MartenAuditRecordCanonicalizer>();
        MartenAuditHashSchemes.Sha256V1.LinkRule.Should().BeOfType<PreviousHashLinkRule>();
        MartenAuditHashSchemes.Sha256V1.Hasher.Should().BeOfType<Sha256AuditHasher>();
    }

    [Fact]
    public void Hash_chain_computes_a_stable_lowercase_hash()
    {
        var first = MartenAuditHashChain.ComputeHash(ChainedDocument());
        var second = MartenAuditHashChain.ComputeHash(ChainedDocument());

        first.Should().Be(second);
        first.Should().HaveLength(64);
        first.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Segment_head_round_trip_preserves_every_field()
    {
        var stored = new MartenAuditSegmentHead
        {
            Id = "orders/2026",
            TenantId = "tenant-a",
            Attributes = new Dictionary<string, string> { ["class"] = "orders" },
            OpenedAt = OccurredAt,
            LastSequence = 4,
            LastHash = "hash-4",
            Count = 4,
            LastCheckpointSequence = 2,
            LastCheckpointAt = OccurredAt.AddHours(1),
            HashScheme = "sha256-v1",
            Expired = false,
            Version = 3
        };

        var head = stored.ToAuditSegmentHead();
        var round = MartenAuditSegmentHead.FromAuditSegmentHead(head);

        round.Id.Should().Be("orders/2026");
        round.TenantId.Should().Be("tenant-a");
        round.Attributes.Should().BeEquivalentTo(stored.Attributes);
        round.OpenedAt.Should().Be(OccurredAt);
        round.LastSequence.Should().Be(4);
        round.LastHash.Should().Be("hash-4");
        round.Count.Should().Be(4);
        round.LastCheckpointSequence.Should().Be(2);
        round.LastCheckpointAt.Should().Be(OccurredAt.AddHours(1));
        round.Expired.Should().BeFalse();
        round.Version.Should().Be(3);
        head.Info.Key.Should().Be("orders/2026");
    }

    [Fact]
    public void Checkpoint_round_trip_preserves_every_field()
    {
        var checkpoint = new AuditCheckpoint(
            Guid.NewGuid(),
            "orders/2026",
            4,
            4,
            "hash-4",
            "sha256-v1",
            AuditCheckpointKind.Regular,
            OccurredAt)
        {
            Signature = new AuditCheckpointSignature("hmac-sha256", "signature-value"),
            Label = "nightly",
            PublishState = CheckpointPublishState.Pending,
            PublishAttempts = 2,
            LastPublishError = "the anchor refused the connection",
            Signatures =
            [
                new AuditCheckpointSignature("hmac-sha256", "signature-value"),
                new AuditCheckpointSignature("timestamp", "timestamp-value")
            ]
        };

        var round = MartenAuditCheckpoint.FromAuditCheckpoint(checkpoint).ToAuditCheckpoint();

        round.Id.Should().Be(checkpoint.Id);
        round.Segment.Should().Be("orders/2026");
        round.Sequence.Should().Be(4);
        round.Count.Should().Be(4);
        round.Hash.Should().Be("hash-4");
        round.HashScheme.Should().Be("sha256-v1");
        round.Kind.Should().Be(AuditCheckpointKind.Regular);
        round.CreatedAt.Should().Be(OccurredAt);
        round.Signature.Should().Be(checkpoint.Signature);
        round.Label.Should().Be("nightly");
        round.PublishState.Should().Be(CheckpointPublishState.Pending);
        round.PublishAttempts.Should().Be(2);
        round.LastPublishError.Should().Be("the anchor refused the connection");
        round.Signatures.Should().BeEquivalentTo(checkpoint.Signatures);
    }

    private static MartenAuditRecord ChainedDocument()
    {
        return new MartenAuditRecord
        {
            Id = Guid.Parse("d3a4b2c1-8f6e-4a1b-9c2d-5e7f0a1b3c4d"),
            Action = "orders.place-order",
            ActorId = "account-7",
            ActorKind = AuditActor.UserKind,
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = OccurredAt,
            Duration = TimeSpan.FromMilliseconds(12),
            Category = "orders",
            TargetKind = "order",
            TargetId = "order-9",
            CorrelationId = "correlation-1",
            TenantId = "tenant-a",
            Segment = "orders/2026",
            Sequence = 3,
            PreviousHash = "predecessor-hash",
            Properties = new Dictionary<string, string>
            {
                ["order-total"] = "9.50",
                ["warehouse"] = "amsterdam"
            }
        };
    }
}
