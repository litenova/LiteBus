using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Storage.UnitTests.Marten;

/// <summary>
///     Verifies the Marten audit trail options: the default segment, hash chain enablement, and validation.
/// </summary>
public sealed class MartenAuditTrailOptionsTests
{
    [Fact]
    public void Defaults_write_plain_records_with_no_chain_and_no_publisher()
    {
        var options = new MartenAuditTrailOptions();

        options.HashChainEnabled.Should().BeFalse();
        options.CheckpointPublisher.Should().BeNull();
        options.CheckpointInterval.Should().Be(MartenAuditTrailOptions.DefaultCheckpointInterval);
        options.CheckpointInterval.Should().Be(100);
    }

    [Fact]
    public void Default_segment_is_the_category_and_year()
    {
        var record = new AuditRecord
        {
            Action = "orders.place-order",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            Category = "orders"
        };

        MartenAuditTrailOptions.DefaultSegment(record).Should().Be("orders/2026");
    }

    [Fact]
    public void Default_segment_without_a_category_uses_the_uncategorized_segment()
    {
        var record = new AuditRecord
        {
            Action = "orders.place-order",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero)
        };

        MartenAuditTrailOptions.DefaultSegment(record).Should().Be("uncategorized/2026");
    }

    [Fact]
    public void Default_segment_rejects_a_null_record()
    {
        var segment = () => MartenAuditTrailOptions.DefaultSegment(null!);
        segment.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UseHashChain_without_a_selector_enables_the_chain_with_the_default_segment()
    {
        var options = new MartenAuditTrailOptions();

        var chained = options.UseHashChain();

        chained.Should().BeSameAs(options);
        options.HashChainEnabled.Should().BeTrue();

        var record = new AuditRecord
        {
            Action = "orders.place-order",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            Category = "orders"
        };

        options.SegmentOf(record).Should().Be("orders/2026");
    }

    [Fact]
    public void UseHashChain_with_a_selector_uses_it_for_every_record()
    {
        var options = new MartenAuditTrailOptions();

        options.UseHashChain(record => $"tenant/{record.TenantId}");

        var record = new AuditRecord
        {
            Action = "orders.place-order",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            TenantId = "tenant-a"
        };

        options.HashChainEnabled.Should().BeTrue();
        options.SegmentOf(record).Should().Be("tenant/tenant-a");
    }

    [Fact]
    public void Validate_accepts_a_positive_checkpoint_interval()
    {
        var options = new MartenAuditTrailOptions { CheckpointInterval = 1 };

        var validate = () => options.Validate();
        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_rejects_a_non_positive_checkpoint_interval(int interval)
    {
        var options = new MartenAuditTrailOptions { CheckpointInterval = interval };

        var validate = () => options.Validate();
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }
}
