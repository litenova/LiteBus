using LiteBus.Auditing.Capture;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.UnitTests.Capture;

/// <summary>
///     Verifies that the capture coordinator writes each record through its capture mode, and that records without a
///     business change always take the out-of-band path.
/// </summary>
public sealed class AuditCaptureCoordinatorTests
{
    /// <summary>
    ///     Every non-success outcome paired with every capture mode.
    /// </summary>
    public static IEnumerable<object[]> NonSuccessOutcomesAndCaptures()
    {
        var outcomes = new[]
        {
            AuditOutcome.Denied, AuditOutcome.Invalid, AuditOutcome.Failed, AuditOutcome.Canceled
        };
        var captures = new AuditCapture[]
        {
            AuditCapture.WithChange(),
            AuditCapture.WithChange(AuditLink.InTransaction),
            AuditCapture.AfterCommitThroughOutbox(),
            AuditCapture.BeforeAndOutcome()
        };

        foreach (var outcome in outcomes)
        {
            foreach (var capture in captures)
            {
                yield return [outcome, capture];
            }
        }
    }

    [Fact]
    public async Task A_success_record_with_WithChange_is_written_to_the_trail()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var coordinator = new AuditCaptureCoordinator(trail, outbox);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        await coordinator.CaptureAsync(record, AuditCapture.WithChange(), CancellationToken.None)
            .ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Subject.Should().Be(record);
        outbox.AuditItems.Should().BeEmpty();
    }

    [Fact]
    public async Task A_success_record_with_WithChange_InTransaction_is_written_to_the_trail()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var coordinator = new AuditCaptureCoordinator(trail, outbox);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        await coordinator.CaptureAsync(record, AuditCapture.WithChange(AuditLink.InTransaction), CancellationToken.None)
            .ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Subject.Should().Be(record);
        outbox.AuditItems.Should().BeEmpty();
    }

    [Fact]
    public async Task A_success_record_with_BeforeAndOutcome_is_written_to_the_trail()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var coordinator = new AuditCaptureCoordinator(trail, outbox);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        await coordinator.CaptureAsync(record, AuditCapture.BeforeAndOutcome(), CancellationToken.None)
            .ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Subject.Should().Be(record);
        outbox.AuditItems.Should().BeEmpty();
    }

    [Fact]
    public async Task A_success_record_with_AfterCommitThroughOutbox_is_enqueued_with_the_record_id()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var coordinator = new AuditCaptureCoordinator(trail, outbox);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded, tenantId: "tenant-9");

        await coordinator.CaptureAsync(record, AuditCapture.AfterCommitThroughOutbox(), CancellationToken.None)
            .ConfigureAwait(false);

        trail.Records.Should().BeEmpty();
        var item = outbox.AuditItems.Should().ContainSingle().Subject;
        item.Message.Record.Should().Be(record);
        item.Metadata.Identity.Should().Be(new MessageIdentity.Supplied(record.Id));
        item.Metadata.Idempotency.Should().Be(new Idempotency.Keyed(record.Id.ToString()));
        item.Metadata.Tenant.Should().Be(new TenantScope.Isolated("tenant-9"));
    }

    [Fact]
    public async Task An_enqueued_record_without_a_tenant_is_unscoped()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var coordinator = new AuditCaptureCoordinator(trail, outbox);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        await coordinator.CaptureAsync(record, AuditCapture.AfterCommitThroughOutbox(), CancellationToken.None)
            .ConfigureAwait(false);

        outbox.AuditItems.Should().ContainSingle().Subject.Metadata.Tenant
            .Should().Be(TenantScope.Unscoped.Instance);
    }

    [Fact]
    public async Task An_enqueued_record_is_assigned_an_id_when_it_has_none()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var coordinator = new AuditCaptureCoordinator(trail, outbox);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded, id: Guid.Empty);

        await coordinator.CaptureAsync(record, AuditCapture.AfterCommitThroughOutbox(), CancellationToken.None)
            .ConfigureAwait(false);

        var enqueued = outbox.AuditItems.Should().ContainSingle().Subject.Message.Record;
        enqueued.Id.Should().NotBe(Guid.Empty);
        outbox.AuditItems.Should().ContainSingle().Subject.Metadata.Identity
            .Should().Be(new MessageIdentity.Supplied(enqueued.Id));
    }

    [Fact]
    public async Task Capturing_through_the_outbox_without_an_outbox_fails_the_mediation()
    {
        var trail = new RecordingAuditTrail();
        var coordinator = new AuditCaptureCoordinator(trail, outbox: null);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        var act = async () => await coordinator
            .CaptureAsync(record, AuditCapture.AfterCommitThroughOutbox(), CancellationToken.None)
            .ConfigureAwait(false);

        (await act.Should().ThrowAsync<AuditConfigurationException>().ConfigureAwait(false))
            .WithMessage("*outbox*");
        trail.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task An_outbox_configuration_fault_is_a_composition_fault()
    {
        var coordinator = new AuditCaptureCoordinator(new RecordingAuditTrail(), outbox: null);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        var act = async () => await coordinator
            .CaptureAsync(record, AuditCapture.AfterCommitThroughOutbox(), CancellationToken.None)
            .ConfigureAwait(false);

        (await act.Should().ThrowAsync<LiteBusConfigurationException>().ConfigureAwait(false))
            .WithMessage("*outbox*");
    }

    [Theory]
    [MemberData(nameof(NonSuccessOutcomesAndCaptures))]
    public async Task Non_success_records_are_written_to_the_trail_in_every_mode(
        AuditOutcome outcome,
        AuditCapture capture)
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var coordinator = new AuditCaptureCoordinator(trail, outbox);
        var record = TestAuditRecords.WithOutcome(outcome);

        await coordinator.CaptureAsync(record, capture, CancellationToken.None).ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Subject.Should().Be(record);
        outbox.AuditItems.Should().BeEmpty();
    }

    [Fact]
    public async Task An_intent_record_is_written_to_the_trail_without_being_enqueued()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var coordinator = new AuditCaptureCoordinator(trail, outbox);
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Intent);

        await coordinator.CaptureAsync(record, AuditCapture.AfterCommitThroughOutbox(), CancellationToken.None)
            .ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Subject.Should().Be(record);
        outbox.AuditItems.Should().BeEmpty();
    }

    [Fact]
    public async Task An_invalid_capture_and_link_pair_is_rejected()
    {
        var coordinator = new AuditCaptureCoordinator(new RecordingAuditTrail(), new RecordingAuditOutbox());
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        var act = async () => await coordinator
            .CaptureAsync(record, AuditCapture.WithChange(AuditLink.AtWrite), CancellationToken.None)
            .ConfigureAwait(false);

        (await act.Should().ThrowAsync<AuditConfigurationException>().ConfigureAwait(false))
            .WithMessage("*AtWrite*");
    }

    [Fact]
    public async Task Capturing_without_a_record_throws()
    {
        var coordinator = new AuditCaptureCoordinator(new RecordingAuditTrail(), new RecordingAuditOutbox());

        var act = async () => await coordinator
            .CaptureAsync(null!, AuditCapture.WithChange(), CancellationToken.None)
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Capturing_without_a_capture_mode_throws()
    {
        var coordinator = new AuditCaptureCoordinator(new RecordingAuditTrail(), new RecordingAuditOutbox());
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        var act = async () => await coordinator
            .CaptureAsync(record, null!, CancellationToken.None)
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }
}
