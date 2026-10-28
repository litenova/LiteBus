using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that the mediation harness runs the shipped pipeline over hand-supplied handlers and reports which
///     stages ran.
/// </summary>
/// <remarks>
///     Asserting that a guard denies previously meant booting the whole host, which for an application with a
///     relational store meant a database container for a test about one authorization decision. Asserting which stages
///     ran is the part no consumer can build, and it is exactly what a test wants when the point of the library is
///     that behavior moved into named stages.
/// </remarks>
[Collection("Sequential")]
public sealed class MediationHarnessTests : LiteBusTestBase
{
    [Fact]
    public async Task A_guard_denial_stops_at_the_guard_stage()
    {
        var recorder = new StageActivityRecorder { Deny = true };

        var result = await MediationHarness.For<SteeredCommand>()
            .With(
                new SteeredGuard<SteeredCommand>(recorder),
                new SteeredValidator<SteeredCommand>(recorder),
                new SteeredShortcut(recorder),
                new SteeredPreHandler(recorder),
                new SteeredCommandHandler(recorder))
            .RunAsync(new SteeredCommand())
            .ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.Reason.Should().Be("not permitted");
        result.Code.Should().Be("NOT_PERMITTED");

        // The assertion the whole harness exists for.
        result.StagesRun.Should().Equal(PreStage.Guard);
        result.MainHandlerRan.Should().BeFalse();
    }

    [Fact]
    public async Task A_clean_run_reports_every_stage_that_had_a_handler()
    {
        var recorder = new StageActivityRecorder();

        var result = await MediationHarness.For<SteeredCommand>()
            .With(
                new SteeredGuard<SteeredCommand>(recorder),
                new SteeredValidator<SteeredCommand>(recorder),
                new SteeredShortcut(recorder),
                new SteeredPreHandler(recorder),
                new SteeredCommandHandler(recorder))
            .RunAsync(new SteeredCommand())
            .ConfigureAwait(false);

        result.IsSuccess.Should().BeTrue();
        result.MainHandlerRan.Should().BeTrue();
        result.StagesRun.Should().Equal(
            PreStage.Guard, PreStage.Validator, PreStage.Shortcut, PreStage.PreHandler);
    }

    [Fact]
    public async Task A_stage_with_no_handler_does_not_appear()
    {
        var recorder = new StageActivityRecorder();

        var result = await MediationHarness.For<SteeredCommand>()
            .With(new SteeredGuard<SteeredCommand>(recorder), new SteeredCommandHandler(recorder))
            .RunAsync(new SteeredCommand())
            .ConfigureAwait(false);

        // What happened, not what could have. An empty stage is skipped by the pipeline and skipped here.
        result.StagesRun.Should().Equal(PreStage.Guard);
    }

    [Fact]
    public async Task A_validation_failure_stops_at_the_validator_and_carries_every_failure()
    {
        var recorder = new StageActivityRecorder { Invalid = true };

        var result = await MediationHarness.For<SteeredCommand>()
            .With(
                new SteeredGuard<SteeredCommand>(recorder),
                new SteeredValidator<SteeredCommand>(recorder),
                new SteeredCommandHandler(recorder))
            .RunAsync(new SteeredCommand())
            .ConfigureAwait(false);

        result.IsInvalid.Should().BeTrue();
        result.StagesRun.Should().Equal(PreStage.Guard, PreStage.Validator);
        result.Failures.Should().ContainSingle().Which.Member.Should().Be("Amount");
    }

    [Fact]
    public async Task An_answered_message_stops_at_the_shortcut_and_is_a_success()
    {
        var recorder = new StageActivityRecorder { Answer = true };

        var result = await MediationHarness.For<SteeredCommand>()
            .With(
                new SteeredGuard<SteeredCommand>(recorder),
                new SteeredShortcut(recorder),
                new SteeredCommandHandler(recorder))
            .RunAsync(new SteeredCommand())
            .ConfigureAwait(false);

        // Nothing was refused, so an answer is a success, and the main handler still did not run.
        result.IsSuccess.Should().BeTrue();
        result.Outcome.Should().Be(MediationOutcome.Answered);
        result.MainHandlerRan.Should().BeFalse();
        result.StagesRun.Should().Equal(PreStage.Guard, PreStage.Shortcut);
        result.Code.Should().Be("ALREADY_APPLIED");
    }

    [Fact]
    public async Task A_result_producing_message_reports_the_value_it_produced()
    {
        var result = await MediationHarness.For<SteeredResultCommand>()
            .With(new SteeredResultCommandHandler())
            .RunAsync<string>(new SteeredResultCommand())
            .ConfigureAwait(false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("handled");
    }

    [Fact]
    public async Task The_harness_honours_the_fixed_stage_order_whatever_order_handlers_are_added()
    {
        var recorder = new StageActivityRecorder();

        await MediationHarness.For<SteeredCommand>()
            .With(
                new SteeredPreHandler(recorder),
                new SteeredShortcut(recorder),
                new SteeredValidator<SteeredCommand>(recorder),
                new SteeredGuard<SteeredCommand>(recorder),
                new SteeredCommandHandler(recorder))
            .RunAsync(new SteeredCommand())
            .ConfigureAwait(false);

        // Registration order cannot reorder the stages. That is the guarantee the harness has to reproduce, since a
        // harness running its own approximation of the pipeline would prove nothing about the real one.
        recorder.Ran.Should().Equal("guard", "validator", "shortcut", "pre-handler", "main");
    }

    [Fact]
    public async Task Evaluate_runs_the_decision_stages_and_nothing_else()
    {
        var recorder = new StageActivityRecorder();

        var decision = await MediationHarness.For<SteeredCommand>()
            .With(
                new SteeredGuard<SteeredCommand>(recorder),
                new SteeredValidator<SteeredCommand>(recorder),
                new SteeredShortcut(recorder),
                new SteeredCommandHandler(recorder))
            .EvaluateAsync(new SteeredCommand())
            .ConfigureAwait(false);

        decision.IsAllowed.Should().BeTrue();
        recorder.Ran.Should().Equal("guard", "validator");
    }

    [Fact]
    public async Task A_genuine_fault_still_propagates()
    {
        var recorder = new StageActivityRecorder();

        var act = async () => await MediationHarness.For<SteeredCommand>()
            .With(new SteeredCommandHandler(recorder), new ThrowingSteeredPostHandler())
            .RunAsync(new SteeredCommand())
            .ConfigureAwait(false);

        // A refusal is a value in the result; a fault is a failed test.
        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Two_harnesses_share_nothing()
    {
        var first = new StageActivityRecorder { Deny = true };
        var second = new StageActivityRecorder();

        var denied = await MediationHarness.For<SteeredCommand>()
            .With(new SteeredGuard<SteeredCommand>(first), new SteeredCommandHandler(first))
            .RunAsync(new SteeredCommand())
            .ConfigureAwait(false);

        var allowed = await MediationHarness.For<SteeredCommand>()
            .With(new SteeredCommandHandler(second))
            .RunAsync(new SteeredCommand())
            .ConfigureAwait(false);

        // One registry per harness, so a guard registered in one test cannot reach the next.
        denied.IsDenied.Should().BeTrue();
        allowed.IsSuccess.Should().BeTrue();
        allowed.StagesRun.Should().BeEmpty();
    }
}
