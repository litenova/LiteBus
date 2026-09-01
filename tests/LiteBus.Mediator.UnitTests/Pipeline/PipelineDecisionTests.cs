using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Covers the shape of a pipeline decision and the two ways a refusal can be created, which are the invariants the
///     rest of the pipeline relies on when it stops a message.
/// </summary>
public sealed class PipelineDecisionTests
{
    [Fact]
    public void Continue_is_the_default_and_stops_nothing()
    {
        // Returning the default is the common path, so it has to be the one that allocates nothing and decides nothing.
        PipelineDecision.Continue.Should().Be(default(PipelineDecision));
        PipelineDecision.Continue.StopsPipeline.Should().BeFalse();
        PipelineDecision.Continue.IsRefusal.Should().BeFalse();
    }

    [Fact]
    public void A_denying_verdict_becomes_a_denied_refusal()
    {
        var decision = Deny("no second approver", "SECOND_APPROVER");

        decision.StopsPipeline.Should().BeTrue();
        decision.Outcome.Should().Be(MediationOutcome.Denied);
        decision.IsRefusal.Should().BeTrue();
        decision.HasResult.Should().BeFalse();

        var refusal = decision.ToRefusal();
        refusal.IsDenied.Should().BeTrue();
        refusal.Reason.Should().Be("no second approver");
        refusal.Code.Should().Be("SECOND_APPROVER");
    }

    [Fact]
    public void An_invalid_validity_becomes_an_invalid_refusal()
    {
        var decision = Invalid(Validity.Invalid("amount must be positive", "Amount", "AMOUNT"));

        decision.Outcome.Should().Be(MediationOutcome.Invalid);
        decision.IsRefusal.Should().BeTrue();
        decision.Failures.Should().ContainSingle();

        var refusal = decision.ToRefusal();

        // The two refusals are kept apart all the way to the mapper, because a denial is what a security review reads
        // and a malformed message is not.
        refusal.IsDenied.Should().BeFalse();
        refusal.Outcome.Should().Be(MediationOutcome.Invalid);
    }

    [Fact]
    public void An_answer_is_not_a_refusal()
    {
        var decision = Answer("already applied");

        decision.StopsPipeline.Should().BeTrue();
        decision.Outcome.Should().Be(MediationOutcome.Answered);

        // Answering denied nobody. Treating it as a refusal would put an entry in the denial list that never happened.
        decision.IsRefusal.Should().BeFalse();
    }

    [Fact]
    public void Describing_a_non_refusal_as_a_refusal_is_rejected()
    {
        var answered = Answer("already applied");

        var describe = () => answered.ToRefusal();

        describe.Should().Throw<InvalidOperationException>().WithMessage("*not a refusal*");
    }

    [Fact]
    public void A_refusal_can_only_be_created_as_denied_or_invalid()
    {
        // The public surface is two factories, so no caller can build a refusal reporting Succeeded, Answered, Failed,
        // or Canceled, none of which a mapper could act on.
        typeof(Refusal).GetConstructors().Should().BeEmpty();

        Refusal.Denied("nope").Outcome.Should().Be(MediationOutcome.Denied);
        Refusal.Invalid("malformed").Outcome.Should().Be(MediationOutcome.Invalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_refusal_without_a_reason_is_rejected(string reason)
    {
        // The reason is the only explanation a stopped mediation leaves anywhere, since it reaches neither
        // post-handlers nor error handlers.
        var denied = () => Refusal.Denied(reason);
        var invalid = () => Refusal.Invalid(reason);

        denied.Should().Throw<ArgumentException>();
        invalid.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Both_shortcut_shapes_answer_with_the_same_verb()
    {
        Shortcut.Answer("done").IsAnswered.Should().BeTrue();
        Shortcut<string>.Answer("value", "cached").IsAnswered.Should().BeTrue();

        Shortcut.None.IsAnswered.Should().BeFalse();
        Shortcut<string>.None.IsAnswered.Should().BeFalse();
    }

    [Fact]
    public void An_untyped_answer_on_a_result_message_names_the_typed_contract()
    {
        var decision = Answer("already applied");

        // Reachable only through the untyped contract on a message that produces a result, which analyzer LB1019
        // reports at build time. The runtime message has to name the fix for anyone who suppressed it.
        var resolve = () => decision.ResolveResult<string>(typeof(PipelineDecisionTests));

        resolve.Should().Throw<LiteBus.Runtime.Abstractions.Exceptions.LiteBusConfigurationException>()
            .WithMessage("*IMessageShortcut*");
    }

    /// <summary>
    ///     Produces a denying decision the way the guard stage does, through the verdict a guard returns.
    /// </summary>
    /// <param name="reason">The reason the guard gave.</param>
    /// <param name="code">The code the guard gave.</param>
    /// <returns>The decision the stage runner acts on.</returns>
    private static PipelineDecision Deny(string reason, string? code)
    {
        return RunThroughStage(Verdict.Deny(reason, code));
    }

    /// <summary>
    ///     Produces an invalid decision the way the validator stage does.
    /// </summary>
    /// <param name="validity">The validity a validator returned.</param>
    /// <returns>The decision the stage runner acts on.</returns>
    private static PipelineDecision Invalid(Validity validity)
    {
        return PipelineDecision.Invalid(validity.Failures);
    }

    /// <summary>
    ///     Produces an answering decision the way the shortcut stage does.
    /// </summary>
    /// <param name="reason">The reason the shortcut gave.</param>
    /// <returns>The decision the stage runner acts on.</returns>
    private static PipelineDecision Answer(string reason)
    {
        return PipelineDecision.Answered(reason, hasResult: false, result: null);
    }

    /// <summary>
    ///     Converts a verdict the way the guard invoker does.
    /// </summary>
    /// <param name="verdict">The verdict a guard returned.</param>
    /// <returns>The decision the stage runner acts on.</returns>
    private static PipelineDecision RunThroughStage(Verdict verdict)
    {
        return verdict.IsDenied
            ? PipelineDecision.Denied(verdict.Reason!, verdict.Code)
            : PipelineDecision.Continue;
    }
}
