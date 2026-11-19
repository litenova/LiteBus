using LiteBus.Auditing.Capture;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.UnitTests.Capture;

/// <summary>
///     Verifies that the capturing trail captures each record with its mediation's resolved mode, falling back to
///     the builder default.
/// </summary>
public sealed class AuditCaptureTrailTests
{
    [Fact]
    public async Task A_record_is_captured_with_its_mediation_mode()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var capturing = new AuditCaptureTrail(
            new AuditCaptureCoordinator(trail, outbox),
            AuditCapture.WithChange());
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);
        var executionContext = new FakeExecutionContext();
        AuditCaptureResolver.StoreResolvedCapture(executionContext, AuditCapture.AfterCommitThroughOutbox());
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        await capturing.WriteAsync(record, CancellationToken.None).ConfigureAwait(false);

        trail.Records.Should().BeEmpty();
        outbox.AuditItems.Should().ContainSingle().Subject.Message.Record.Should().Be(record);
    }

    [Fact]
    public async Task A_record_without_a_mediation_uses_the_builder_default()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var capturing = new AuditCaptureTrail(
            new AuditCaptureCoordinator(trail, outbox),
            AuditCapture.AfterCommitThroughOutbox());
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);

        AmbientExecutionContext.ResetForTesting();

        await capturing.WriteAsync(record, CancellationToken.None).ConfigureAwait(false);

        trail.Records.Should().BeEmpty();
        outbox.AuditItems.Should().ContainSingle().Subject.Message.Record.Should().Be(record);
    }

    [Fact]
    public async Task A_record_for_an_unresolved_mediation_uses_the_builder_default()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var capturing = new AuditCaptureTrail(
            new AuditCaptureCoordinator(trail, outbox),
            AuditCapture.WithChange());
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Succeeded);
        using var _ = AmbientExecutionContext.CreateScope(new FakeExecutionContext());

        await capturing.WriteAsync(record, CancellationToken.None).ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Subject.Should().Be(record);
        outbox.AuditItems.Should().BeEmpty();
    }

    [Fact]
    public async Task A_denial_uses_the_trail_even_when_the_mediation_resolved_the_outbox()
    {
        var trail = new RecordingAuditTrail();
        var outbox = new RecordingAuditOutbox();
        var capturing = new AuditCaptureTrail(
            new AuditCaptureCoordinator(trail, outbox),
            AuditCapture.WithChange());
        var record = TestAuditRecords.WithOutcome(AuditOutcome.Denied);
        var executionContext = new FakeExecutionContext();
        AuditCaptureResolver.StoreResolvedCapture(executionContext, AuditCapture.AfterCommitThroughOutbox());
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        await capturing.WriteAsync(record, CancellationToken.None).ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Subject.Should().Be(record);
        outbox.AuditItems.Should().BeEmpty();
    }

    [Fact]
    public void An_invalid_builder_default_is_rejected_at_construction()
    {
        var coordinator = new AuditCaptureCoordinator(new RecordingAuditTrail(), new RecordingAuditOutbox());

        var act = () => new AuditCaptureTrail(coordinator, AuditCapture.WithChange(AuditLink.AtWrite));

        act.Should().Throw<AuditConfigurationException>().WithMessage("*the auditing builder*AtWrite*");
    }

    [Fact]
    public async Task Writing_without_a_record_throws()
    {
        var capturing = new AuditCaptureTrail(
            new AuditCaptureCoordinator(new RecordingAuditTrail(), new RecordingAuditOutbox()),
            AuditCapture.WithChange());

        var act = async () => await capturing.WriteAsync(null!, CancellationToken.None).ConfigureAwait(false);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }
}
