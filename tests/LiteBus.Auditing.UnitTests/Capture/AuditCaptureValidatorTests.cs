using LiteBus.Auditing.Capture;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.UnitTests.Capture;

/// <summary>
///     Verifies that capture and link pairs are validated, and that each mode reports its linking and outbox
///     behavior.
/// </summary>
public sealed class AuditCaptureValidatorTests
{
    [Fact]
    public void WithChange_with_AfterCommit_is_valid()
    {
        var act = () => AuditCaptureValidator.Validate(AuditCapture.WithChange(AuditLink.AfterCommit), "test");

        act.Should().NotThrow();
    }

    [Fact]
    public void WithChange_defaults_to_AfterCommit()
    {
        AuditCaptureValidator.EffectiveLink(AuditCapture.WithChange()).Should().Be(AuditLink.AfterCommit);
    }

    [Fact]
    public void WithChange_with_InTransaction_is_valid()
    {
        var act = () => AuditCaptureValidator.Validate(AuditCapture.WithChange(AuditLink.InTransaction), "test");

        act.Should().NotThrow();
    }

    [Fact]
    public void AfterCommitThroughOutbox_is_valid()
    {
        var act = () => AuditCaptureValidator.Validate(AuditCapture.AfterCommitThroughOutbox(), "test");

        act.Should().NotThrow();
    }

    [Fact]
    public void BeforeAndOutcome_is_valid()
    {
        var act = () => AuditCaptureValidator.Validate(AuditCapture.BeforeAndOutcome(), "test");

        act.Should().NotThrow();
    }

    [Fact]
    public void WithChange_with_AtWrite_is_rejected_and_names_the_source()
    {
        var act = () => AuditCaptureValidator.Validate(
            AuditCapture.WithChange(AuditLink.AtWrite),
            "message 'PlaceOrderCommand'");

        act.Should().Throw<AuditConfigurationException>()
            .WithMessage("*message 'PlaceOrderCommand'*AtWrite*AfterCommit*InTransaction*");
    }

    [Fact]
    public void WithChange_with_an_unknown_link_value_is_rejected()
    {
        var act = () => AuditCaptureValidator.Validate(
            AuditCapture.WithChange((AuditLink) 99),
            "the auditing builder");

        act.Should().Throw<AuditConfigurationException>()
            .WithMessage("*the auditing builder*99*");
    }

    [Fact]
    public void A_rejected_pair_is_a_composition_fault()
    {
        var act = () => AuditCaptureValidator.Validate(
            AuditCapture.WithChange(AuditLink.AtWrite),
            "the auditing builder");

        act.Should().Throw<LiteBusConfigurationException>();
    }

    [Fact]
    public void Only_AfterCommitThroughOutbox_requires_the_outbox()
    {
        AuditCaptureValidator.RequiresOutbox(AuditCapture.AfterCommitThroughOutbox()).Should().BeTrue();
        AuditCaptureValidator.RequiresOutbox(AuditCapture.WithChange()).Should().BeFalse();
        AuditCaptureValidator.RequiresOutbox(AuditCapture.WithChange(AuditLink.InTransaction)).Should().BeFalse();
        AuditCaptureValidator.RequiresOutbox(AuditCapture.BeforeAndOutcome()).Should().BeFalse();
    }

    [Fact]
    public void Each_mode_reports_its_linking()
    {
        AuditCaptureValidator.EffectiveLink(AuditCapture.WithChange(AuditLink.AfterCommit))
            .Should().Be(AuditLink.AfterCommit);
        AuditCaptureValidator.EffectiveLink(AuditCapture.WithChange(AuditLink.InTransaction))
            .Should().Be(AuditLink.InTransaction);
        AuditCaptureValidator.EffectiveLink(AuditCapture.AfterCommitThroughOutbox()).Should().Be(AuditLink.AtWrite);
        AuditCaptureValidator.EffectiveLink(AuditCapture.BeforeAndOutcome()).Should().Be(AuditLink.AtWrite);
    }

    [Fact]
    public void Validating_without_a_capture_mode_throws()
    {
        var act = () => AuditCaptureValidator.Validate(null!, "test");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validating_without_a_source_throws()
    {
        var act = () => AuditCaptureValidator.Validate(AuditCapture.WithChange(), "  ");

        act.Should().Throw<ArgumentException>();
    }
}
