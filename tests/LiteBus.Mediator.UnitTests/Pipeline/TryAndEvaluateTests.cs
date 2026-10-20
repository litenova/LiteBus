using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that a refusal can be received as a value rather than caught as an exception, and that a message can
///     be evaluated without being performed.
/// </summary>
/// <remarks>
///     v7 changed validators specifically so an expected control-flow path stays off the exception path, and then the
///     boundary still had to catch one to produce a 403. These two methods finish that.
/// </remarks>
[Collection("Sequential")]
public sealed class TryAndEvaluateTests : LiteBusTestBase
{
    [Fact]
    public async Task TrySendAsync_reports_a_denial_as_a_value()
    {
        var recorder = new StageActivityRecorder { Deny = true };
        var provider = Build(recorder);

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new SteeredCommand()).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Reason.Should().Be("not permitted");
        result.Code.Should().Be("NOT_PERMITTED");
    }

    [Fact]
    public async Task TrySendAsync_reports_a_validation_failure_with_every_failure()
    {
        var recorder = new StageActivityRecorder { Invalid = true };
        var provider = Build(recorder);

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new SteeredCommand()).ConfigureAwait(false);

        result.IsInvalid.Should().BeTrue();
        result.Failures.Should().ContainSingle();
        result.Failures[0].Member.Should().Be("Amount");
        result.Code.Should().Be("AMOUNT_POSITIVE");
    }

    [Fact]
    public async Task TrySendAsync_reports_success_and_runs_the_whole_pipeline()
    {
        var recorder = new StageActivityRecorder();
        var provider = Build(recorder);

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new SteeredCommand()).ConfigureAwait(false);

        result.IsSuccess.Should().BeTrue();
        result.Outcome.Should().Be(MediationOutcome.Succeeded);
        recorder.Ran.Should().Equal("guard", "validator", "shortcut", "pre-handler", "main");
    }

    [Fact]
    public async Task TrySendAsync_reports_an_answer_as_a_success_carrying_its_code()
    {
        var recorder = new StageActivityRecorder { Answer = true };
        var provider = Build(recorder);

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new SteeredCommand()).ConfigureAwait(false);

        // Nothing was refused, so an answer is a success. The code is what tells it apart from ordinary success.
        result.IsSuccess.Should().BeTrue();
        result.Outcome.Should().Be(MediationOutcome.Answered);
        result.Code.Should().Be("ALREADY_APPLIED");
        recorder.Ran.Should().NotContain("main");
    }

    [Fact]
    public async Task TrySendAsync_carries_the_value_a_command_produced()
    {
        var provider = Build(new StageActivityRecorder());

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new SteeredResultCommand()).ConfigureAwait(false);

        result.IsSuccess.Should().BeTrue();
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("handled");
    }

    [Fact]
    public async Task TrySendAsync_reports_a_denial_with_no_value_when_no_mapper_is_registered()
    {
        var recorder = new StageActivityRecorder { Deny = true };
        var provider = Build(recorder);

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new SteeredResultCommand()).ConfigureAwait(false);

        // A refusal does not owe the caller the value the handler would have produced.
        result.IsDenied.Should().BeTrue();
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task TrySendAsync_reports_a_denial_with_the_value_a_refusal_mapper_supplied()
    {
        var recorder = new StageActivityRecorder { Deny = true };
        var provider = Build(recorder, typeof(SteeredRefusalMapper));

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new SteeredResultCommand()).ConfigureAwait(false);

        // The mapper returns a value rather than raising, so reading the outcome from the exception would have called
        // this a success. Both halves arrive: the application's shape and the framework's classification.
        result.IsDenied.Should().BeTrue();
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("refused:NOT_PERMITTED");
    }

    [Fact]
    public async Task TrySendAsync_still_propagates_a_genuine_fault()
    {
        var provider = Build(new StageActivityRecorder(), typeof(ThrowingSteeredPostHandler));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new SteeredCommand()).ConfigureAwait(false);

        // A fault is not something a boundary should branch on, so the line stays where the pipeline draws it.
        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task TryQueryAsync_reports_a_denial_as_a_value()
    {
        var recorder = new StageActivityRecorder { Deny = true };
        var provider = Build(recorder);

        var result = await provider.GetRequiredService<IQueryMediator>()
            .TryQueryAsync(new SteeredQuery()).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.Code.Should().Be("NOT_PERMITTED");
    }

    [Fact]
    public async Task EvaluateAsync_runs_the_decision_stages_and_nothing_else()
    {
        var recorder = new StageActivityRecorder();
        var provider = Build(recorder);

        var decision = await provider.GetRequiredService<ICommandMediator>()
            .EvaluateAsync(new SteeredCommand()).ConfigureAwait(false);

        decision.IsAllowed.Should().BeTrue();

        // The shortcut and the pre-handler act rather than decide. Running them to answer "may I" would claim an
        // idempotency key for a message nobody submitted.
        recorder.Ran.Should().Equal("guard", "validator");
    }

    [Fact]
    public async Task EvaluateAsync_reports_the_denial_the_pipeline_would_report()
    {
        var recorder = new StageActivityRecorder { Deny = true };
        var provider = Build(recorder);

        var decision = await provider.GetRequiredService<ICommandMediator>()
            .EvaluateAsync(new SteeredCommand()).ConfigureAwait(false);

        // The same reason and code a sent command would carry, which is the point: one question, one answer.
        decision.IsDenied.Should().BeTrue();
        decision.Reason.Should().Be("not permitted");
        decision.Code.Should().Be("NOT_PERMITTED");
        recorder.Ran.Should().Equal("guard");
    }

    [Fact]
    public async Task EvaluateAsync_reports_a_malformed_message_with_its_failures()
    {
        var recorder = new StageActivityRecorder { Invalid = true };
        var provider = Build(recorder);

        var decision = await provider.GetRequiredService<ICommandMediator>()
            .EvaluateAsync(new SteeredCommand()).ConfigureAwait(false);

        decision.IsInvalid.Should().BeTrue();
        decision.Failures.Should().ContainSingle().Which.Member.Should().Be("Amount");
    }

    [Fact]
    public async Task EvaluateAsync_does_not_perform_the_message()
    {
        var recorder = new StageActivityRecorder();
        var provider = Build(recorder);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.EvaluateAsync(new SteeredCommand()).ConfigureAwait(false);
        await mediator.EvaluateAsync(new SteeredCommand()).ConfigureAwait(false);

        recorder.Ran.Should().NotContain("main");
    }

    [Fact]
    public async Task EvaluateAsync_answers_for_a_query_as_well()
    {
        var recorder = new StageActivityRecorder { Deny = true };
        var provider = Build(recorder);

        var decision = await provider.GetRequiredService<IQueryMediator>()
            .EvaluateAsync(new SteeredQuery()).ConfigureAwait(false);

        decision.IsDenied.Should().BeTrue();
    }

    /// <summary>
    ///     Builds a provider over the steered command, its result-producing sibling, and the steered query.
    /// </summary>
    /// <param name="recorder">The recorder shared with every stage.</param>
    /// <param name="extras">Extra command types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build(StageActivityRecorder recorder, params Type[] extras)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<SteeredCommand>();
                    builder.Register<SteeredCommandHandler>();
                    builder.Register<SteeredResultCommand>();
                    builder.Register<SteeredResultCommandHandler>();
                    builder.Register(typeof(SteeredGuard<>));
                    builder.Register(typeof(SteeredValidator<>));
                    builder.Register<SteeredShortcut>();
                    builder.Register<SteeredPreHandler>();

                    foreach (var extra in extras)
                    {
                        builder.Register(extra);
                    }
                });

                registry.AddQueries(builder =>
                {
                    builder.Register<SteeredQuery>();
                    builder.Register<SteeredQueryHandler>();
                    builder.Register(typeof(SteeredQueryGuard<>));
                    builder.Register(typeof(SteeredQueryValidator<>));
                });
            })
            .BuildServiceProvider();
    }
}
