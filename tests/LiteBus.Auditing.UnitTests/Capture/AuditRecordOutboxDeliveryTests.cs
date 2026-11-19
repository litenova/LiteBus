using LiteBus.Auditing.Capture;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.UnitTests.Capture;

/// <summary>
///     Verifies that audit records travel through the transactional outbox to the audit store, idempotent by record
///     id.
/// </summary>
public sealed class AuditRecordOutboxDeliveryTests
{
    [Fact]
    public async Task An_enqueued_record_is_written_to_the_store_on_delivery()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var coordinator = new AuditCaptureCoordinator(trail, outbox);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        await coordinator.CaptureAsync(record, AuditCapture.AfterCommitThroughOutbox(), CancellationToken.None)
            .ConfigureAwait(false);

        var delivered = outbox.AuditItems.Should().ContainSingle().Subject.Message;
        var handler = new AuditRecordOutboxHandler(trail);

        await handler.HandleAsync(delivered, CancellationToken.None).ConfigureAwait(false);

        var stored = trail.Records.Should().ContainSingle().Subject;
        stored.Should().Be(record);
        stored.Id.Should().Be(record.Id);
    }

    [Fact]
    public async Task A_redelivered_record_keeps_its_id()
    {
        var trail = new RecordingAuditTrail();
        var handler = new AuditRecordOutboxHandler(trail);
        var message = new AuditRecordOutboxMessage(TestAuditRecords.WithOutcome(AuditOutcome.Succeeded));

        await handler.HandleAsync(message, CancellationToken.None).ConfigureAwait(false);
        await handler.HandleAsync(message, CancellationToken.None).ConfigureAwait(false);

        var stored = trail.Records.ToArray();
        stored.Should().HaveCount(2);
        stored[0].Id.Should().Be(message.Record.Id);
        stored[1].Id.Should().Be(message.Record.Id);
    }

    [Fact]
    public void The_carrier_contract_is_stable()
    {
        var contract = typeof(AuditRecordOutboxMessage)
            .GetCustomAttributes(typeof(MessageContractAttribute), inherit: false)
            .Should().ContainSingle().Subject as MessageContractAttribute;

        contract.Should().NotBeNull();
        contract!.Name.Should().Be("litebus.audit-record");
        contract.Version.Should().Be(1);
    }

    [Fact]
    public void The_carrier_is_exempt_from_auditing()
    {
        typeof(AuditRecordOutboxMessage)
            .GetCustomAttributes(typeof(AuditExemptAttribute), inherit: false)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Delivering_without_a_message_throws()
    {
        var handler = new AuditRecordOutboxHandler(new RecordingAuditTrail());

        var act = async () => await handler.HandleAsync(null!, CancellationToken.None).ConfigureAwait(false);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }
}
