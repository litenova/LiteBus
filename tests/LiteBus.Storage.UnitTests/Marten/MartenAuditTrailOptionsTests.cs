using System.Text;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Messaging.Audit;

namespace LiteBus.Storage.UnitTests.Marten;

/// <summary>
///     Verifies the Marten audit trail options: defaults, the default segmenter, hash chain enablement, and hash chain builder validation.
/// </summary>
public sealed class MartenAuditTrailOptionsTests
{
    [Fact]
    public void Defaults_write_plain_records_with_no_chain_and_the_default_scan_interval()
    {
        var options = new MartenAuditTrailOptions();

        options.HashChainEnabled.Should().BeFalse();
        options.CheckpointScanInterval.Should().Be(MartenAuditTrailOptions.DefaultCheckpointScanInterval);
        options.CheckpointScanInterval.Should().Be(TimeSpan.FromMinutes(5));
        MartenAuditTrailOptions.DefaultCheckpointInterval.Should().Be(100);
    }

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
    public void UseHashChain_without_configuration_enables_the_chain_with_the_defaults()
    {
        var options = new MartenAuditTrailOptions();

        MartenAuditTrailOptions? chained = null;
        var enable = () => chained = options.UseHashChain();

        enable.Should().NotThrow();
        chained.Should().BeSameAs(options);
        options.HashChainEnabled.Should().BeTrue();
    }

    [Fact]
    public void UseHashChain_with_a_null_configuration_enables_the_chain()
    {
        var options = new MartenAuditTrailOptions();

        options.UseHashChain(null);

        options.HashChainEnabled.Should().BeTrue();
    }

    [Fact]
    public void UseHashChain_accepts_a_custom_segmenter_delegate()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.SegmentBy<MartenAuditRecord>(document => $"tenant/{document.TenantId}"));

        enable.Should().NotThrow();
        options.HashChainEnabled.Should().BeTrue();
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
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.CheckpointWhen(CheckpointPolicies.EveryRecords(10)));

        enable.Should().NotThrow();
        options.HashChainEnabled.Should().BeTrue();
    }

    [Fact]
    public void UseHashChain_accepts_a_time_based_checkpoint_policy()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.CheckpointWhen(CheckpointPolicies.Every(TimeSpan.FromHours(1))));

        enable.Should().NotThrow();
        options.HashChainEnabled.Should().BeTrue();
    }

    [Fact]
    public void UseHashChain_rejects_a_null_checkpoint_policy()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder => builder.CheckpointWhen(null!));
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
    public void UseHashChain_accepts_a_second_scheme_for_older_records()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.AcceptScheme(TestScheme("test-sha256-v2")));

        enable.Should().NotThrow();
        options.HashChainEnabled.Should().BeTrue();
    }

    [Fact]
    public void UseHashChain_with_a_new_write_scheme_keeps_accepting_the_previous_scheme()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder => builder
            .UseScheme(TestScheme("test-sha256-v2"))
            .AcceptScheme(MartenAuditHashSchemes.Sha256V1));

        enable.Should().NotThrow();
        options.HashChainEnabled.Should().BeTrue();
    }

    [Fact]
    public void UseHashChain_rejects_two_different_schemes_sharing_a_name()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.AcceptScheme(TestScheme(MartenAuditHashSchemes.Sha256V1.Name)));

        enable.Should().Throw<InvalidOperationException>().WithMessage("*unique*");
    }

    [Fact]
    public void UseScheme_rejects_a_null_scheme()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder => builder.UseScheme((AuditHashScheme<MartenAuditRecord>) null!));
        enable.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AcceptScheme_rejects_a_null_scheme()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder => builder.AcceptScheme((AuditHashScheme<MartenAuditRecord>) null!));
        enable.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseHashChain_accepts_a_write_scheme_factory()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.UseScheme<MartenAuditRecord>(_ => TestScheme("test-factory-v1")));

        enable.Should().NotThrow();
        options.HashChainEnabled.Should().BeTrue();
    }

    [Fact]
    public void UseHashChain_accepts_an_accepted_scheme_factory()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.AcceptScheme<MartenAuditRecord>(_ => TestScheme("test-accepted-factory-v1")));

        enable.Should().NotThrow();
        options.HashChainEnabled.Should().BeTrue();
    }

    [Fact]
    public void UseScheme_rejects_a_null_factory()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.UseScheme<MartenAuditRecord>((Func<IServiceProvider, AuditHashScheme<MartenAuditRecord>>) null!));
        enable.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AcceptScheme_rejects_a_null_factory()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.AcceptScheme<MartenAuditRecord>((Func<IServiceProvider, AuditHashScheme<MartenAuditRecord>>) null!));
        enable.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseHashChain_stores_a_custom_document_with_its_mapper_segmenter_and_scheme()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder => builder
            .StoreAs<CustomAuditDocument>(new CustomAuditDocumentMapper())
            .SegmentBy<CustomAuditDocument>(new CustomAuditDocumentSegmenter())
            .UseScheme(new AuditHashScheme<CustomAuditDocument>(
                "custom-v1",
                new CustomAuditDocumentCanonicalizer(),
                new PreviousHashLinkRule(),
                new Sha256AuditHasher())));

        enable.Should().NotThrow();
        options.HashChainEnabled.Should().BeTrue();
    }

    [Fact]
    public void UseHashChain_rejects_a_custom_document_without_a_segmenter()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.StoreAs<CustomAuditDocument, CustomAuditDocumentMapper>());

        enable.Should().Throw<InvalidOperationException>().WithMessage("*segmenter*");
    }

    [Fact]
    public void UseHashChain_rejects_a_custom_document_without_a_scheme()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder => builder
            .StoreAs<CustomAuditDocument>(new CustomAuditDocumentMapper())
            .SegmentBy<CustomAuditDocument>(new CustomAuditDocumentSegmenter()));

        enable.Should().Throw<InvalidOperationException>().WithMessage("*hash scheme*");
    }

    [Fact]
    public void StoreAs_rejects_a_null_mapper()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.StoreAs<CustomAuditDocument>(null!));

        enable.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void StoreAs_rejects_a_second_document_type()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder => builder
            .StoreAs<MartenAuditRecord>(new MartenAuditRecordMapper())
            .StoreAs<CustomAuditDocument>(new CustomAuditDocumentMapper()));

        enable.Should().Throw<InvalidOperationException>().WithMessage("*one document type*");
    }

    [Fact]
    public void SegmentBy_rejects_a_segmenter_for_another_document_type()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder => builder
            .StoreAs<CustomAuditDocument>(new CustomAuditDocumentMapper())
            .SegmentBy<MartenAuditRecord>(new MartenAuditRecordSegmenter()));

        enable.Should().Throw<InvalidOperationException>().WithMessage("*SegmentBy named*");
    }

    [Fact]
    public void SegmentBy_rejects_a_null_segmenter()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.SegmentBy<MartenAuditRecord>((IAuditSegmenter<MartenAuditRecord>) null!));

        enable.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SegmentBy_rejects_a_null_delegate()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.SegmentBy<MartenAuditRecord>((Func<MartenAuditRecord, string>) null!));

        enable.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseHashChain_signs_checkpoints_with_the_configured_signer()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.SignCheckpointsWith(new TestCheckpointSigner()));

        enable.Should().NotThrow();
        options.HashChainEnabled.Should().BeTrue();
    }

    [Fact]
    public void SignCheckpointsWith_rejects_a_null_signer()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder => builder.SignCheckpointsWith(null!));
        enable.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseHashChain_publishes_checkpoints_to_the_configured_publisher()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder =>
            builder.PublishCheckpointsTo(new TestCheckpointPublisher()));

        enable.Should().NotThrow();
        options.HashChainEnabled.Should().BeTrue();
    }

    [Fact]
    public void PublishCheckpointsTo_rejects_a_null_publisher()
    {
        var options = new MartenAuditTrailOptions();

        var enable = () => options.UseHashChain(builder => builder.PublishCheckpointsTo(null!));
        enable.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_accepts_a_positive_scan_interval()
    {
        var options = new MartenAuditTrailOptions { CheckpointScanInterval = TimeSpan.FromSeconds(30) };

        var validate = () => options.Validate();
        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Validate_rejects_a_non_positive_scan_interval(int seconds)
    {
        var options = new MartenAuditTrailOptions { CheckpointScanInterval = TimeSpan.FromSeconds(seconds) };

        var validate = () => options.Validate();
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static AuditHashScheme<MartenAuditRecord> TestScheme(string name)
    {
        return new AuditHashScheme<MartenAuditRecord>(
            name,
            new MartenAuditRecordCanonicalizer(),
            new PreviousHashLinkRule(),
            new Sha256AuditHasher());
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

    private sealed class CustomAuditDocumentMapper : IAuditRecordMapper<CustomAuditDocument>
    {
        public CustomAuditDocument Map(AuditRecord record)
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

    private sealed class CustomAuditDocumentCanonicalizer : IAuditCanonicalizer<CustomAuditDocument>
    {
        public byte[] Canonicalize(CustomAuditDocument document)
        {
            return Encoding.UTF8.GetBytes(document.Action ?? string.Empty);
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
