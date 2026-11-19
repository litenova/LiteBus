using System.Security.Claims;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Capture;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.UnitTests.Capture;

/// <summary>
///     Verifies that the capture pre-handler resolves the mode for each audited mediation and writes the intent
///     record for the BeforeAndOutcome mode.
/// </summary>
public sealed class AuditCapturePreHandlerTests
{
    [Fact]
    public async Task An_unaudited_message_resolves_nothing_and_writes_nothing()
    {
        var reader = new FakeMessageReader()
            .Add(typeof(CaptureTestCommand), new FakeMessageMetadata());
        var trail = new RecordingAuditTrail();
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            new AuditingOptions(),
            reader,
            trail,
            new FakeAuditScope(),
            TimeProvider.System);
        var executionContext = new FakeExecutionContext();
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        AuditCaptureResolver.ReadResolvedCapture(executionContext).Should().BeNull();
        trail.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task An_exempt_message_resolves_nothing_and_writes_nothing()
    {
        var reader = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set<AuditDeclaration>(AuditDeclaration.Exempt("not sensitive")));
        var trail = new RecordingAuditTrail();
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            new AuditingOptions(),
            reader,
            trail,
            new FakeAuditScope(),
            TimeProvider.System);
        var executionContext = new FakeExecutionContext();
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        AuditCaptureResolver.ReadResolvedCapture(executionContext).Should().BeNull();
        trail.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_message_resolves_nothing_and_writes_nothing()
    {
        var trail = new RecordingAuditTrail();
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            new AuditingOptions(),
            new FakeMessageReader(),
            trail,
            new FakeAuditScope(),
            TimeProvider.System);
        var executionContext = new FakeExecutionContext();
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        AuditCaptureResolver.ReadResolvedCapture(executionContext).Should().BeNull();
        trail.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task An_audited_message_stores_its_capture_without_an_intent_record()
    {
        var reader = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set<AuditDeclaration>(AuditDeclaration.Audited("orders.place-order")));
        var trail = new RecordingAuditTrail();
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            new AuditingOptions(),
            reader,
            trail,
            new FakeAuditScope(),
            TimeProvider.System);
        var executionContext = new FakeExecutionContext();
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        AuditCaptureResolver.ReadResolvedCapture(executionContext).Should().Be(AuditCapture.WithChange());
        trail.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task A_per_message_override_is_stored_for_the_mediation()
    {
        var metadata = new FakeMessageMetadata()
            .Set<AuditDeclaration>(AuditDeclaration.Audited("orders.place-order"))
            .Set(new AuditCaptureDeclaration(AuditCapture.AfterCommitThroughOutbox()));
        var reader = new FakeMessageReader().Add(typeof(CaptureTestCommand), metadata);
        var trail = new RecordingAuditTrail();
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            new AuditingOptions(),
            reader,
            trail,
            new FakeAuditScope(),
            TimeProvider.System);
        var executionContext = new FakeExecutionContext();
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        AuditCaptureResolver.ReadResolvedCapture(executionContext)
            .Should().Be(AuditCapture.AfterCommitThroughOutbox());
        trail.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task A_store_that_cannot_join_the_transaction_is_adjusted_for_the_mediation()
    {
        var reader = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set<AuditDeclaration>(AuditDeclaration.Audited("orders.place-order")));
        var trail = new RecordingAuditTrail();
        var options = new AuditingOptions
        {
            StoreCapabilities = new AuditStoreCapabilities(JoinsBusinessTransaction: false)
        };
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            options,
            reader,
            trail,
            new FakeAuditScope(),
            TimeProvider.System);
        var executionContext = new FakeExecutionContext();
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        AuditCaptureResolver.ReadResolvedCapture(executionContext)
            .Should().Be(AuditCapture.AfterCommitThroughOutbox());
        trail.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task BeforeAndOutcome_writes_an_intent_record_before_the_handler_runs()
    {
        var reader = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set<AuditDeclaration>(
                    AuditDeclaration.Audited("orders.place-order", category: "money", targetKind: "order")));
        var trail = new RecordingAuditTrail();
        var options = new AuditingOptions { Capture = AuditCapture.BeforeAndOutcome() };
        var executionContext = new FakeExecutionContext();
        executionContext.Items[MessageTraceContextKeys.CorrelationId] = "correlation-1";
        executionContext.Items[MessageTraceContextKeys.TenantId] = "tenant-9";
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            options,
            reader,
            trail,
            new FakeAuditScope(),
            TimeProvider.System);
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        AuditCaptureResolver.ReadResolvedCapture(executionContext).Should().Be(AuditCapture.BeforeAndOutcome());
        var intent = trail.Records.Should().ContainSingle().Subject;
        intent.Id.Should().NotBe(Guid.Empty);
        intent.Action.Should().Be("orders.place-order");
        intent.Outcome.Should().Be(AuditOutcome.Intent);
        intent.Category.Should().Be("money");
        intent.TargetKind.Should().Be("order");
        intent.Duration.Should().Be(TimeSpan.Zero);
        intent.FailureCode.Should().BeNull();
        intent.MessageType.Should().Be(typeof(CaptureTestCommand).FullName);
        intent.CorrelationId.Should().Be("correlation-1");
        intent.TenantId.Should().Be("tenant-9");
        intent.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task An_intent_record_reads_selectors_off_the_message()
    {
        AuditDeclaration declaration = AuditDeclaration.Audited("orders.place-order")
            .WithTargetFrom<CaptureTestCommand>(_ => "order-42")
            .WithReasonFrom<CaptureTestCommand>(_ => "customer requested");
        var reader = new FakeMessageReader()
            .Add(typeof(CaptureTestCommand), new FakeMessageMetadata().Set(declaration));
        var trail = new RecordingAuditTrail();
        var options = new AuditingOptions { Capture = AuditCapture.BeforeAndOutcome() };
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            options,
            reader,
            trail,
            new FakeAuditScope(),
            TimeProvider.System);
        using var _ = AmbientExecutionContext.CreateScope(new FakeExecutionContext());

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        var intent = trail.Records.Should().ContainSingle().Subject;
        intent.TargetId.Should().Be("order-42");
        intent.Reason.Should().Be("customer requested");
    }

    [Fact]
    public async Task An_intent_record_prefers_scope_values_over_selectors()
    {
        AuditDeclaration declaration = AuditDeclaration.Audited("orders.place-order")
            .WithTargetFrom<CaptureTestCommand>(_ => "order-42")
            .WithReasonFrom<CaptureTestCommand>(_ => "customer requested");
        var reader = new FakeMessageReader()
            .Add(typeof(CaptureTestCommand), new FakeMessageMetadata().Set(declaration));
        var trail = new RecordingAuditTrail();
        var options = new AuditingOptions { Capture = AuditCapture.BeforeAndOutcome() };
        var scope = new FakeAuditScope
        {
            Actor = AuditActor.User("acct-override"),
            TargetId = "order-override",
            Reason = "override reason",
            Properties = new Dictionary<string, string> { ["channel"] = "web" }
        };
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            options,
            reader,
            trail,
            scope,
            TimeProvider.System);
        using var _ = AmbientExecutionContext.CreateScope(new FakeExecutionContext());

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        var intent = trail.Records.Should().ContainSingle().Subject;
        intent.Actor.Should().Be(AuditActor.User("acct-override"));
        intent.TargetId.Should().Be("order-override");
        intent.Reason.Should().Be("override reason");
        intent.Properties.Should().ContainKey("channel").WhoseValue.Should().Be("web");
    }

    [Fact]
    public async Task An_intent_record_attributes_the_calling_principal()
    {
        var reader = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set<AuditDeclaration>(AuditDeclaration.Audited("orders.place-order")));
        var trail = new RecordingAuditTrail();
        var options = new AuditingOptions { Capture = AuditCapture.BeforeAndOutcome() };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "acct-7"),
            new Claim(ClaimTypes.Name, "Seven")
        ]));
        var executionContext = new FakeExecutionContext
        {
            Identity = ExecutionIdentity.For(principal)
        };
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            options,
            reader,
            trail,
            new FakeAuditScope(),
            TimeProvider.System);
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        var intent = trail.Records.Should().ContainSingle().Subject;
        intent.Actor.Should().Be(AuditActor.User("acct-7") with { DisplayName = "Seven" });
    }

    [Fact]
    public async Task An_intent_record_falls_back_to_the_subject_claim()
    {
        var reader = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set<AuditDeclaration>(AuditDeclaration.Audited("orders.place-order")));
        var trail = new RecordingAuditTrail();
        var options = new AuditingOptions { Capture = AuditCapture.BeforeAndOutcome() };
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "acct-9")]));
        var executionContext = new FakeExecutionContext
        {
            Identity = ExecutionIdentity.For(principal)
        };
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            options,
            reader,
            trail,
            new FakeAuditScope(),
            TimeProvider.System);
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        trail.Records.Should().ContainSingle().Subject.Actor.Should().Be(AuditActor.User("acct-9"));
    }

    [Fact]
    public async Task An_intent_record_is_still_written_without_a_required_reason()
    {
        var reader = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set<AuditDeclaration>(
                    AuditDeclaration.Audited("orders.place-order", reasonRequired: true)));
        var trail = new RecordingAuditTrail();
        var options = new AuditingOptions { Capture = AuditCapture.BeforeAndOutcome() };
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            options,
            reader,
            trail,
            new FakeAuditScope(),
            TimeProvider.System);
        using var _ = AmbientExecutionContext.CreateScope(new FakeExecutionContext());

        await handler.PreHandleAsync(new CaptureTestCommand(), CancellationToken.None).ConfigureAwait(false);

        var intent = trail.Records.Should().ContainSingle().Subject;
        intent.Outcome.Should().Be(AuditOutcome.Intent);
        intent.Reason.Should().BeNull();
    }

    [Fact]
    public async Task PreHandling_without_a_mediation_throws()
    {
        var reader = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set<AuditDeclaration>(AuditDeclaration.Audited("orders.place-order")));
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            new AuditingOptions(),
            reader,
            new RecordingAuditTrail(),
            new FakeAuditScope(),
            TimeProvider.System);

        AmbientExecutionContext.ResetForTesting();

        var act = async () => await handler
            .PreHandleAsync(new CaptureTestCommand(), CancellationToken.None)
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<NoExecutionContextException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task PreHandling_without_a_message_throws()
    {
        var handler = new AuditCapturePreHandler<CaptureTestCommand>(
            new AuditingOptions(),
            new FakeMessageReader(),
            new RecordingAuditTrail(),
            new FakeAuditScope(),
            TimeProvider.System);

        var act = async () => await handler
            .PreHandleAsync(null!, CancellationToken.None)
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }
}
