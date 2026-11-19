using LiteBus.Auditing.Capture;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.UnitTests.Capture;

/// <summary>
///     Verifies that the builder default, the per-message override, and the store capabilities resolve to one
///     effective capture mode.
/// </summary>
public sealed class AuditCaptureResolverTests
{
    [Fact]
    public void A_message_without_an_override_keeps_the_builder_default()
    {
        var registry = new FakeMessageReader()
            .Add(typeof(CaptureTestCommand), new FakeMessageMetadata());

        var resolution = AuditCaptureResolver.Resolve(
            typeof(CaptureTestCommand),
            registry,
            AuditCapture.WithChange(),
            new AuditStoreCapabilities(JoinsBusinessTransaction: true));

        resolution.Requested.Should().Be(AuditCapture.WithChange());
        resolution.Effective.Should().Be(AuditCapture.WithChange());
        resolution.OverridingMessageType.Should().BeNull();
        resolution.OutboxSelectedBecauseStoreCannotJoinTransaction.Should().BeFalse();
    }

    [Fact]
    public void An_unregistered_message_keeps_the_builder_default()
    {
        var resolution = AuditCaptureResolver.Resolve(
            typeof(CaptureTestCommand),
            new FakeMessageReader(),
            AuditCapture.BeforeAndOutcome(),
            new AuditStoreCapabilities(JoinsBusinessTransaction: true));

        resolution.Requested.Should().Be(AuditCapture.BeforeAndOutcome());
        resolution.Effective.Should().Be(AuditCapture.BeforeAndOutcome());
        resolution.OverridingMessageType.Should().BeNull();
    }

    [Fact]
    public void A_per_message_override_wins_over_the_builder_default()
    {
        var registry = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set(new AuditCaptureDeclaration(AuditCapture.BeforeAndOutcome())));

        var resolution = AuditCaptureResolver.Resolve(
            typeof(CaptureTestCommand),
            registry,
            AuditCapture.WithChange(),
            new AuditStoreCapabilities(JoinsBusinessTransaction: true));

        resolution.Requested.Should().Be(AuditCapture.BeforeAndOutcome());
        resolution.Effective.Should().Be(AuditCapture.BeforeAndOutcome());
        resolution.OverridingMessageType.Should().Be<CaptureTestCommand>();
        resolution.OutboxSelectedBecauseStoreCannotJoinTransaction.Should().BeFalse();
    }

    [Fact]
    public void A_per_message_override_can_restore_WithChange()
    {
        var registry = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set(new AuditCaptureDeclaration(AuditCapture.WithChange())));

        var resolution = AuditCaptureResolver.Resolve(
            typeof(CaptureTestCommand),
            registry,
            AuditCapture.AfterCommitThroughOutbox(),
            new AuditStoreCapabilities(JoinsBusinessTransaction: true));

        resolution.Requested.Should().Be(AuditCapture.WithChange());
        resolution.Effective.Should().Be(AuditCapture.WithChange());
        resolution.OverridingMessageType.Should().Be<CaptureTestCommand>();
    }

    [Fact]
    public void An_override_on_a_base_type_applies_to_a_derived_message()
    {
        var registry = new FakeMessageReader()
            .Add(
                typeof(CaptureTestBaseCommand),
                new FakeMessageMetadata().Set(new AuditCaptureDeclaration(AuditCapture.BeforeAndOutcome())));

        var resolution = AuditCaptureResolver.Resolve(
            typeof(CaptureTestDerivedCommand),
            registry,
            AuditCapture.WithChange(),
            new AuditStoreCapabilities(JoinsBusinessTransaction: true));

        resolution.Requested.Should().Be(AuditCapture.BeforeAndOutcome());
        resolution.Effective.Should().Be(AuditCapture.BeforeAndOutcome());
        resolution.OverridingMessageType.Should().Be<CaptureTestDerivedCommand>();
    }

    [Fact]
    public void WithChange_selects_the_outbox_when_the_store_cannot_join_the_transaction()
    {
        var registry = new FakeMessageReader()
            .Add(typeof(CaptureTestCommand), new FakeMessageMetadata());

        var resolution = AuditCaptureResolver.Resolve(
            typeof(CaptureTestCommand),
            registry,
            AuditCapture.WithChange(),
            new AuditStoreCapabilities(JoinsBusinessTransaction: false));

        resolution.Requested.Should().Be(AuditCapture.WithChange());
        resolution.Effective.Should().Be(AuditCapture.AfterCommitThroughOutbox());
        resolution.OverridingMessageType.Should().BeNull();
        resolution.OutboxSelectedBecauseStoreCannotJoinTransaction.Should().BeTrue();
    }

    [Fact]
    public void An_override_is_adjusted_when_the_store_cannot_join_the_transaction()
    {
        var registry = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set(
                    new AuditCaptureDeclaration(AuditCapture.WithChange(AuditLink.InTransaction))));

        var resolution = AuditCaptureResolver.Resolve(
            typeof(CaptureTestCommand),
            registry,
            AuditCapture.WithChange(),
            new AuditStoreCapabilities(JoinsBusinessTransaction: false));

        resolution.Requested.Should().Be(AuditCapture.WithChange(AuditLink.InTransaction));
        resolution.Effective.Should().Be(AuditCapture.AfterCommitThroughOutbox());
        resolution.OverridingMessageType.Should().Be<CaptureTestCommand>();
        resolution.OutboxSelectedBecauseStoreCannotJoinTransaction.Should().BeTrue();
    }

    [Fact]
    public void Modes_avoiding_the_transaction_are_left_alone_when_the_store_cannot_join_it()
    {
        var registry = new FakeMessageReader()
            .Add(typeof(CaptureTestCommand), new FakeMessageMetadata());
        var capabilities = new AuditStoreCapabilities(JoinsBusinessTransaction: false);

        var outbox = AuditCaptureResolver.Resolve(
            typeof(CaptureTestCommand),
            registry,
            AuditCapture.AfterCommitThroughOutbox(),
            capabilities);

        outbox.Effective.Should().Be(AuditCapture.AfterCommitThroughOutbox());
        outbox.OutboxSelectedBecauseStoreCannotJoinTransaction.Should().BeFalse();

        var beforeAndOutcome = AuditCaptureResolver.Resolve(
            typeof(CaptureTestCommand),
            registry,
            AuditCapture.BeforeAndOutcome(),
            capabilities);

        beforeAndOutcome.Effective.Should().Be(AuditCapture.BeforeAndOutcome());
        beforeAndOutcome.OutboxSelectedBecauseStoreCannotJoinTransaction.Should().BeFalse();
    }

    [Fact]
    public void No_store_means_no_adjustment()
    {
        var registry = new FakeMessageReader()
            .Add(typeof(CaptureTestCommand), new FakeMessageMetadata());

        var resolution = AuditCaptureResolver.Resolve(
            typeof(CaptureTestCommand),
            registry,
            AuditCapture.WithChange(),
            capabilities: null);

        resolution.Effective.Should().Be(AuditCapture.WithChange());
        resolution.OutboxSelectedBecauseStoreCannotJoinTransaction.Should().BeFalse();
    }

    [Fact]
    public void An_invalid_builder_default_is_rejected_and_names_the_builder()
    {
        var act = () => AuditCaptureResolver.Resolve(
            typeof(CaptureTestCommand),
            new FakeMessageReader(),
            AuditCapture.WithChange(AuditLink.AtWrite),
            new AuditStoreCapabilities(JoinsBusinessTransaction: true));

        act.Should().Throw<AuditConfigurationException>().WithMessage("*the auditing builder*AtWrite*");
    }

    [Fact]
    public void An_invalid_override_is_rejected_and_names_the_message()
    {
        var registry = new FakeMessageReader()
            .Add(
                typeof(CaptureTestCommand),
                new FakeMessageMetadata().Set(
                    new AuditCaptureDeclaration(AuditCapture.WithChange(AuditLink.AtWrite))));

        var act = () => AuditCaptureResolver.Resolve(
            typeof(CaptureTestCommand),
            registry,
            AuditCapture.WithChange(),
            new AuditStoreCapabilities(JoinsBusinessTransaction: true));

        act.Should().Throw<AuditConfigurationException>().WithMessage("*CaptureTestCommand*AtWrite*");
    }

    [Fact]
    public void The_builder_default_resolves_without_a_message()
    {
        var resolution = AuditCaptureResolver.ResolveDefault(
            AuditCapture.WithChange(),
            new AuditStoreCapabilities(JoinsBusinessTransaction: false));

        resolution.Requested.Should().Be(AuditCapture.WithChange());
        resolution.Effective.Should().Be(AuditCapture.AfterCommitThroughOutbox());
        resolution.OverridingMessageType.Should().BeNull();
        resolution.OutboxSelectedBecauseStoreCannotJoinTransaction.Should().BeTrue();
    }

    [Fact]
    public void A_stored_capture_is_read_back()
    {
        var executionContext = new FakeExecutionContext();

        AuditCaptureResolver.StoreResolvedCapture(executionContext, AuditCapture.BeforeAndOutcome());

        AuditCaptureResolver.ReadResolvedCapture(executionContext).Should().Be(AuditCapture.BeforeAndOutcome());
    }

    [Fact]
    public void Reading_without_a_mediation_returns_no_capture()
    {
        AuditCaptureResolver.ReadResolvedCapture(null).Should().BeNull();
        AuditCaptureResolver.ReadResolvedCapture(new FakeExecutionContext()).Should().BeNull();
    }

    [Fact]
    public void Storing_without_a_context_throws()
    {
        var act = () => AuditCaptureResolver.StoreResolvedCapture(null!, AuditCapture.WithChange());

        act.Should().Throw<ArgumentNullException>();
    }
}
