using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies the value semantics of the types a caller receives instead of an exception.
/// </summary>
/// <remarks>
///     These are readonly structs a boundary switches on, so equality and the predicates are part of the contract
///     rather than incidental: a test comparing two results, or using one as a dictionary key, depends on both.
/// </remarks>
public sealed class MediationResultTests
{
    [Fact]
    public void A_succeeded_result_reports_success_and_nothing_else()
    {
        var result = MediationResult.Succeeded();

        result.Outcome.Should().Be(MediationOutcome.Succeeded);
        result.IsSuccess.Should().BeTrue();
        result.IsDenied.Should().BeFalse();
        result.IsInvalid.Should().BeFalse();
        result.Reason.Should().BeNull();
        result.Code.Should().BeNull();
        result.Failures.Should().BeEmpty();
    }

    [Fact]
    public void An_answered_result_is_a_success()
    {
        var result = MediationResult.Answered("already applied", "ALREADY_APPLIED");

        // Nothing was refused, so an answer is a success. Reporting it as anything else would put an entry in the
        // list a security review reads that never happened.
        result.IsSuccess.Should().BeTrue();
        result.Outcome.Should().Be(MediationOutcome.Answered);
        result.Code.Should().Be("ALREADY_APPLIED");
    }

    [Fact]
    public void A_denied_result_carries_the_reason_and_the_code()
    {
        var result = MediationResult.Denied("not permitted", "NOT_PERMITTED");

        result.IsDenied.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Reason.Should().Be("not permitted");
    }

    [Fact]
    public void An_invalid_result_carries_every_failure()
    {
        var failures = new[] { new ValidationFailure("too small", "Amount"), new ValidationFailure("missing", "Name") };
        var result = MediationResult.Invalid("two problems", code: null, failures);

        result.IsInvalid.Should().BeTrue();
        result.Failures.Should().HaveCount(2);
    }

    [Fact]
    public void Two_results_describing_the_same_ending_are_equal()
    {
        var first = MediationResult.Denied("not permitted", "NOT_PERMITTED");
        var second = MediationResult.Denied("not permitted", "NOT_PERMITTED");

        first.Should().Be(second);
        (first == second).Should().BeTrue();
        (first != second).Should().BeFalse();
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Equals((object) second).Should().BeTrue();
        first.Equals("not a result").Should().BeFalse();
    }

    [Fact]
    public void Two_results_describing_different_endings_differ()
    {
        var denied = MediationResult.Denied("not permitted", "NOT_PERMITTED");
        var invalid = MediationResult.Invalid("malformed", "MALFORMED", []);

        (denied == invalid).Should().BeFalse();
        (denied != invalid).Should().BeTrue();
    }

    [Fact]
    public void A_typed_result_separates_having_a_value_from_succeeding()
    {
        var produced = MediationResult<string>.Succeeded("handled");
        var refused = MediationResult<string>.Denied("not permitted", "NOT_PERMITTED");
        var mapped = MediationResult<string>.Denied("not permitted", "NOT_PERMITTED", "refused", hasValue: true);

        produced.HasValue.Should().BeTrue();
        produced.Value.Should().Be("handled");

        // A refusal does not owe the caller the value the handler would have produced.
        refused.HasValue.Should().BeFalse();

        // Unless a refusal mapper supplied one, in which case both halves arrive.
        mapped.IsDenied.Should().BeTrue();
        mapped.HasValue.Should().BeTrue();
        mapped.Value.Should().Be("refused");
    }

    [Fact]
    public void A_typed_answered_result_carries_its_value_and_is_a_success()
    {
        var result = MediationResult<string>.Answered("cached", "cache hit", "CACHE_HIT");

        result.IsSuccess.Should().BeTrue();
        result.IsInvalid.Should().BeFalse();
        result.Value.Should().Be("cached");
        result.Code.Should().Be("CACHE_HIT");
    }

    [Fact]
    public void A_typed_invalid_result_carries_every_failure()
    {
        var result = MediationResult<string>.Invalid(
            "malformed",
            "MALFORMED",
            [new ValidationFailure("too small", "Amount")]);

        result.IsInvalid.Should().BeTrue();
        result.Failures.Should().ContainSingle();
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Two_typed_results_carrying_the_same_value_are_equal()
    {
        var first = MediationResult<string>.Succeeded("handled");
        var second = MediationResult<string>.Succeeded("handled");
        var other = MediationResult<string>.Succeeded("different");

        first.Should().Be(second);
        (first == second).Should().BeTrue();
        (first != other).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Equals((object) second).Should().BeTrue();
        first.Equals(42).Should().BeFalse();
    }

    [Fact]
    public void An_allowed_decision_is_the_default_so_it_allocates_nothing()
    {
        var decision = MediationDecision.Allowed;

        decision.Should().Be(default(MediationDecision));
        decision.IsAllowed.Should().BeTrue();
        decision.IsDenied.Should().BeFalse();
        decision.IsInvalid.Should().BeFalse();
        decision.Failures.Should().BeEmpty();
        decision.Reason.Should().BeNull();
    }

    [Fact]
    public void A_denied_decision_carries_what_the_guard_supplied()
    {
        var decision = MediationDecision.Denied("not permitted", "NOT_PERMITTED");

        decision.IsDenied.Should().BeTrue();
        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Be("not permitted");
        decision.Code.Should().Be("NOT_PERMITTED");
    }

    [Fact]
    public void An_invalid_decision_carries_every_failure()
    {
        var decision = MediationDecision.Invalid("malformed", "MALFORMED", [new ValidationFailure("too small")]);

        decision.IsInvalid.Should().BeTrue();
        decision.Failures.Should().ContainSingle();
    }

    [Fact]
    public void Two_decisions_reaching_the_same_conclusion_are_equal()
    {
        var first = MediationDecision.Denied("not permitted", "NOT_PERMITTED");
        var second = MediationDecision.Denied("not permitted", "NOT_PERMITTED");

        first.Should().Be(second);
        (first == second).Should().BeTrue();
        (first != MediationDecision.Allowed).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Equals((object) second).Should().BeTrue();
        first.Equals("not a decision").Should().BeFalse();
    }

    [Fact]
    public void A_pipeline_step_renders_its_origin()
    {
        var direct = new MessagePipelineStep("guard", 0, typeof(string), typeof(int), false, false);
        var indirect = new MessagePipelineStep("guard", 0, typeof(string), typeof(int), true, false);
        var closed = new MessagePipelineStep("guard", 0, typeof(string), typeof(int), false, true);
        var both = new MessagePipelineStep("guard", 0, typeof(string), typeof(int), true, true);

        // The origin is what tells a reviewer whether a stage came from a registration they can see.
        direct.ToString().Should().NotContain("(");
        indirect.ToString().Should().Contain("(indirect)");
        closed.ToString().Should().Contain("(open generic)");
        both.ToString().Should().Contain("(open generic, indirect)");
    }

    [Fact]
    public async Task The_harness_runs_under_the_tags_it_was_given()
    {
        var recorder = new StageActivityRecorder();

        var result = await MediationHarness.For<SteeredCommand>()
            .WithTags("audit")
            .With(new SteeredCommandHandler(recorder))
            .RunAsync(new SteeredCommand())
            .ConfigureAwait(false);

        // An untagged handler runs under any tag set, so this asserts the tags reach the mediation without changing
        // which handlers match.
        result.IsSuccess.Should().BeTrue();
        recorder.Ran.Should().Equal("main");
    }
}
