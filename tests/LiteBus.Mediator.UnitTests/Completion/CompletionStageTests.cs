using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     Verifies that the completion stage observes every mediation outcome, including the paths that never reach
///     post-handlers or error handlers.
/// </summary>
[Collection("Sequential")]
public sealed class CompletionStageTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider registering only the completion test types, keeping other axes untouched.
    /// </summary>
    /// <param name="recorder">The recorder shared with the completion handlers.</param>
    /// <param name="completionHandlers">The completion handler types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(CompletionRecorder recorder, params Type[] completionHandlers)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(CompletionCommand));
                    builder.Register(typeof(CompletionCommandWithResult));
                    builder.Register(typeof(CompletionCommandGate));
                    builder.Register(typeof(CompletionCommandHandler));
                    builder.Register(typeof(CompletionCommandWithResultHandler));
                    builder.Register(typeof(CompletionCommandErrorHandler));

                    foreach (var completionHandler in completionHandlers)
                    {
                        builder.Register(completionHandler);
                    }
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Completion_runs_with_Succeeded_when_the_handler_succeeds()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(DirectCompletionHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new CompletionCommand()).ConfigureAwait(false);

        recorder.Observed.Should().ContainSingle();

        var observed = recorder.Observed.Single().Context;
        observed.Outcome.Should().Be(MessageOutcome.Succeeded);
        observed.Exception.Should().BeNull();
        observed.Faulted.Should().BeFalse();
    }

    [Fact]
    public async Task Completion_runs_with_Failed_when_the_handler_throws()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(DirectCompletionHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new CompletionCommand { ShouldThrow = true }).ConfigureAwait(false);

        var observed = recorder.Observed.Single().Context;
        observed.Outcome.Should().Be(MessageOutcome.Failed);
        observed.Exception.Should().BeOfType<InvalidOperationException>();
        observed.Faulted.Should().BeTrue();
    }

    [Fact]
    public async Task Completion_runs_with_Denied_and_carries_the_reason()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(DirectCompletionHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new CompletionCommand { ShouldDeny = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        var observed = recorder.Observed.Single().Context;
        observed.Outcome.Should().Be(MessageOutcome.Denied);
        observed.Reason.Should().Be("not permitted");

        // A denial is a decision, so it is not reported as a fault even though it reaches the caller as an exception.
        observed.Faulted.Should().BeFalse();
    }

    [Fact]
    public async Task Completion_receives_the_result_typed_when_the_handler_asks_for_it()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(TypedResultCompletionHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new CompletionCommandWithResult()).ConfigureAwait(false);

        recorder.TypedResults.Single().Should().Be((true, "produced"));
    }

    [Fact]
    public async Task A_suppressed_completion_fault_is_attached_to_the_original_exception()
    {
        var provider = BuildThrowingObserverProvider();
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new CompletionCommand { ShouldThrow = true }).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        // Losing this would mean losing the reason an audit record was never written.
        var suppressed = thrown.Which.Data[MediationExceptionData.SuppressedCompletionFaults]
            .Should().BeAssignableTo<IReadOnlyList<Exception>>().Subject;

        suppressed.Should().ContainSingle().Which.Should().BeOfType<NotSupportedException>();
    }

    [Fact]
    public async Task Completion_runs_with_Canceled_and_the_cancellation_still_propagates()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(DirectCompletionHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new CompletionCommand { ShouldCancel = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var observed = recorder.Observed.Single().Context;
        observed.Outcome.Should().Be(MessageOutcome.Canceled);
        observed.Exception.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task Completion_observes_the_result_produced_by_the_handler()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(GlobalCompletionHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var result = await mediator.SendAsync(new CompletionCommandWithResult()).ConfigureAwait(false);

        result.Should().Be("produced");
        recorder.Observed.Single().Context.MessageResult.Should().Be("produced");
    }

    [Fact]
    public async Task Direct_completion_handlers_run_before_indirect_ones()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(DirectCompletionHandler), typeof(GlobalCompletionHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new CompletionCommand()).ConfigureAwait(false);

        recorder.Observed.Select(o => o.Handler).Should().Equal("direct", "global");
    }

    [Fact]
    public async Task A_failing_completion_handler_does_not_replace_the_original_fault()
    {
        var provider = BuildThrowingObserverProvider();
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new CompletionCommand { ShouldThrow = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("handler failed").ConfigureAwait(false);
    }

    [Fact]
    public async Task A_failing_completion_handler_propagates_when_the_mediation_succeeded()
    {
        var provider = BuildThrowingObserverProvider();
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new CompletionCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<NotSupportedException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Completion_reports_a_measured_duration()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(DirectCompletionHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new CompletionCommand()).ConfigureAwait(false);

        recorder.Observed.Single().Context.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    /// <summary>
    ///     Builds a provider whose only completion handler always throws, with no error handler registered.
    /// </summary>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildThrowingObserverProvider()
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(CompletionCommand));
                    builder.Register(typeof(CompletionCommandGate));
                    builder.Register(typeof(CompletionCommandHandler));
                    builder.Register(typeof(ThrowingCompletionHandler));
                });
            })
            .BuildServiceProvider();
    }
}
