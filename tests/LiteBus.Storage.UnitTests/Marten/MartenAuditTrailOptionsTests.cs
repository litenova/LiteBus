using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Storage.Marten;

namespace LiteBus.Storage.UnitTests.Marten;

/// <summary>
///     Verifies the Marten audit chain configuration: the default segmenter, the delegate segmenter, checkpoint
///     policy validation, and hash chain builder validation.
/// </summary>
public sealed class MartenAuditTrailOptionsTests
{
    [Fact]
    public void Default_segmenter_places_a_categorized_document_in_the_category_and_year_segment()
    {
        var document = new MartenAuditRecord
        {
            Action = "orders.place-order",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            Category = "orders"
        };

        new MartenAuditRecordSegmenter().SegmentOf(document).Should().Be("orders/2026");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Default_segmenter_places_a_document_without_a_category_in_the_uncategorized_segment(string? category)
    {
        var document = new MartenAuditRecord
        {
            Action = "orders.place-order",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            Category = category
        };

        new MartenAuditRecordSegmenter().SegmentOf(document).Should().Be("uncategorized/2026");
    }

    [Fact]
    public void Default_segmenter_rejects_a_null_document()
    {
        var segment = () => new MartenAuditRecordSegmenter().SegmentOf(null!);
        segment.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Default_segmenter_opens_segments_without_a_genesis_document()
    {
        var document = new MartenAuditRecord
        {
            Action = "orders.place-order",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            Category = "orders"
        };

        new MartenAuditRecordSegmenter().CreateGenesis("orders/2026", document).Should().BeNull();
    }

    [Fact]
    public void Func_segmenter_assigns_each_document_through_the_delegate()
    {
        var segmenter = new MartenAuditFuncSegmenter<MartenAuditRecord>(document => $"tenant/{document.TenantId}");
        var document = new MartenAuditRecord
        {
            Action = "orders.place-order",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            TenantId = "tenant-a"
        };

        segmenter.SegmentOf(document).Should().Be("tenant/tenant-a");
    }

    [Fact]
    public void Func_segmenter_rejects_a_null_delegate()
    {
        var create = () => new MartenAuditFuncSegmenter<MartenAuditRecord>((Func<MartenAuditRecord, string>) null!);
        create.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Func_segmenter_rejects_a_null_document()
    {
        var segmenter = new MartenAuditFuncSegmenter<MartenAuditRecord>(document => $"tenant/{document.TenantId}");

        var segment = () => segmenter.SegmentOf(null!);
        segment.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Func_segmenter_opens_segments_without_a_genesis_document()
    {
        var segmenter = new MartenAuditFuncSegmenter<MartenAuditRecord>(document => $"tenant/{document.TenantId}");
        var document = new MartenAuditRecord
        {
            Action = "orders.place-order",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            TenantId = "tenant-a"
        };

        segmenter.CreateGenesis("tenant/tenant-a", document).Should().BeNull();
    }

    [Fact]
    public void UseHashChain_accepts_a_record_count_checkpoint_policy()
    {
        var builder = new AuditingModuleBuilder();

        var enable = () => builder.UseHashChain(chain =>
            chain.CheckpointWhen(CheckpointPolicies.EveryRecords(10)));

        enable.Should().NotThrow();
    }

    [Fact]
    public void UseHashChain_accepts_a_time_based_checkpoint_policy()
    {
        var builder = new AuditingModuleBuilder();

        var enable = () => builder.UseHashChain(chain =>
            chain.CheckpointWhen(CheckpointPolicies.Every(TimeSpan.FromHours(1))));

        enable.Should().NotThrow();
    }

    [Fact]
    public void UseHashChain_rejects_a_null_checkpoint_policy()
    {
        var builder = new AuditingModuleBuilder();

        var enable = () => builder.UseHashChain(chain => chain.CheckpointWhen(null!));
        enable.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Checkpoint_policies_reject_a_non_positive_record_count(int recordCount)
    {
        var policy = () => CheckpointPolicies.EveryRecords(recordCount);
        policy.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Checkpoint_policies_reject_a_non_positive_interval(int seconds)
    {
        var policy = () => CheckpointPolicies.Every(TimeSpan.FromSeconds(seconds));
        policy.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UseScheme_rejects_a_null_scheme()
    {
        var builder = new AuditingModuleBuilder();

        var enable = () => builder.UseHashChain(chain => chain.UseScheme(null!));
        enable.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseHashChain_stores_a_custom_document_with_its_mapper_segmenter_and_scheme()
    {
        var builder = new AuditingModuleBuilder();

        var enable = () => builder.UseHashChain(chain => chain
            .StoreAs<CustomAuditDocument, CustomAuditDocumentMapper>()
            .SegmentBy<CustomAuditDocumentSegmenter>()
            .UseScheme("custom-v1"));

        enable.Should().NotThrow();
    }

    [Fact]
    public void UseHashChain_signs_checkpoints_with_the_configured_signer()
    {
        var builder = new AuditingModuleBuilder();

        var enable = () => builder.UseHashChain(chain =>
            chain.SignCheckpointsWith<TestCheckpointSigner>());

        enable.Should().NotThrow();
    }

    [Fact]
    public void UseHashChain_publishes_checkpoints_to_the_configured_publisher()
    {
        var builder = new AuditingModuleBuilder();

        var enable = () => builder.UseHashChain(chain =>
            chain.PublishCheckpointsTo<TestCheckpointPublisher>());

        enable.Should().NotThrow();
    }

    private sealed class CustomAuditDocument : IAuditChainDocument
    {
        public string? Action { get; set; }

        public string? Segment { get; set; }

        public long Sequence { get; set; }

        public string? PreviousHash { get; set; }

        public string? Hash { get; set; }

        public string? HashScheme { get; set; }
    }

    private sealed class CustomAuditDocumentMapper : AuditRecordMapper<CustomAuditDocument>
    {
        public override CustomAuditDocument Map(AuditRecord record)
        {
            return new CustomAuditDocument { Action = record.Action };
        }
    }

    private sealed class CustomAuditDocumentSegmenter : IAuditSegmenter<CustomAuditDocument>
    {
        public string SegmentOf(CustomAuditDocument document)
        {
            return "custom";
        }

        public CustomAuditDocument? CreateGenesis(string segment, CustomAuditDocument first)
        {
            return null;
        }
    }

    private sealed class TestCheckpointSigner : ICheckpointSigner
    {
        public ValueTask<AuditCheckpointSignature> SignAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new AuditCheckpointSignature("test", "signature"));
        }

        public ValueTask<bool> VerifyAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(true);
        }
    }

    private sealed class TestCheckpointPublisher : ICheckpointPublisher
    {
        public ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }
}
