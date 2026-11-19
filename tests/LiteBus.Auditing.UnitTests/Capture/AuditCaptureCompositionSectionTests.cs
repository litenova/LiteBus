using System.Text.Json;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Capture;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.UnitTests.Capture;

/// <summary>
///     Verifies that the composition model reports the effective audit capture mode and the store adjustment.
/// </summary>
public sealed class AuditCaptureCompositionSectionTests
{
    [Fact]
    public void The_section_reports_the_default_capture()
    {
        var section = new AuditCaptureCompositionSection(new AuditingOptions());

        section.SectionName.Should().Be("audit-capture");

        var data = section.GetData().Should().BeOfType<Dictionary<string, object>>().Subject;
        data["capture"].Should().Be("WithChange(AfterCommit)");
        data["effectiveCapture"].Should().Be("WithChange(AfterCommit)");
        data["effectiveLink"].Should().Be("AfterCommit");
        data["storeRegistered"].Should().Be(false);
        data["storeJoinsBusinessTransaction"].Should().Be(false);
        data["outboxSelectedBecauseStoreCannotJoinTransaction"].Should().Be(false);
    }

    [Fact]
    public void A_store_that_cannot_join_the_transaction_is_reported()
    {
        var options = new AuditingOptions
        {
            Capture = AuditCapture.WithChange(),
            StoreCapabilities = new AuditStoreCapabilities(JoinsBusinessTransaction: false)
        };
        var section = new AuditCaptureCompositionSection(options);

        var data = section.GetData().Should().BeOfType<Dictionary<string, object>>().Subject;
        data["capture"].Should().Be("WithChange(AfterCommit)");
        data["effectiveCapture"].Should().Be("AfterCommitThroughOutbox");
        data["effectiveLink"].Should().Be("AtWrite");
        data["storeRegistered"].Should().Be(true);
        data["storeJoinsBusinessTransaction"].Should().Be(false);
        data["outboxSelectedBecauseStoreCannotJoinTransaction"].Should().Be(true);
    }

    [Fact]
    public void An_explicit_outbox_mode_is_not_reported_as_an_adjustment()
    {
        var options = new AuditingOptions
        {
            Capture = AuditCapture.AfterCommitThroughOutbox(),
            StoreCapabilities = new AuditStoreCapabilities(JoinsBusinessTransaction: true)
        };
        var section = new AuditCaptureCompositionSection(options);

        var data = section.GetData().Should().BeOfType<Dictionary<string, object>>().Subject;
        data["capture"].Should().Be("AfterCommitThroughOutbox");
        data["effectiveCapture"].Should().Be("AfterCommitThroughOutbox");
        data["effectiveLink"].Should().Be("AtWrite");
        data["storeRegistered"].Should().Be(true);
        data["storeJoinsBusinessTransaction"].Should().Be(true);
        data["outboxSelectedBecauseStoreCannotJoinTransaction"].Should().Be(false);
    }

    [Fact]
    public void BeforeAndOutcome_links_at_write()
    {
        var options = new AuditingOptions { Capture = AuditCapture.BeforeAndOutcome() };
        var section = new AuditCaptureCompositionSection(options);

        var data = section.GetData().Should().BeOfType<Dictionary<string, object>>().Subject;
        data["capture"].Should().Be("BeforeAndOutcome");
        data["effectiveCapture"].Should().Be("BeforeAndOutcome");
        data["effectiveLink"].Should().Be("AtWrite");
    }

    [Fact]
    public void The_section_data_serializes_to_json()
    {
        var options = new AuditingOptions
        {
            StoreCapabilities = new AuditStoreCapabilities(JoinsBusinessTransaction: false)
        };
        var section = new AuditCaptureCompositionSection(options);

        var json = JsonSerializer.Serialize(section.GetData());

        json.Should().Contain("\"effectiveCapture\":\"AfterCommitThroughOutbox\"");
        json.Should().Contain("\"outboxSelectedBecauseStoreCannotJoinTransaction\":true");
    }
}
