using LiteBus.Events;
using LiteBus.Queries;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Auditing;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.FaultRefusals;

/// <summary>
///     Verifies that a fault a classifier recognizes ends the mediation as that refusal instead of a failure.
/// </summary>
[Collection("Sequential")]
public sealed class FaultRefusalTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider with the given messaging and command registrations.
    /// </summary>
    /// <param name="configureMessaging">The messaging configuration, or null for no classifiers.</param>
    /// <param name="configureCommands">The command registrations.</param>
    /// <param name="calls">The recorder shared with the stages.</param>
    /// <param name="completions">The recorder shared with the completion handlers.</param>
    /// <param name="trail">The recording audit trail, when the test asserts on audit records.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildCommandsProvider(
        Action<MessageModuleBuilder>? configureMessaging,
        Action<CommandModuleBuilder> configureCommands,
        FaultRefusalRecorder calls,
        CompletionRecorder completions,
        RecordingAuditTrail? trail = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton(completions);

        if (trail is not null)
        {
            services.AddSingleton<IAuditTrail>(trail);
        }

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging =>
                {
                    configureMessaging?.Invoke(messaging);
                });

                registry.AddCommands(configureCommands);
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Classified_main_fault_returns_a_denial_from_TrySendAsync()
    {
        var calls = new FaultRefusalRecorder();
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<ConflictClassifier>();
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictErrorHandler>();
            },
            calls,
            new CompletionRecorder());

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new ConflictCommand { ThrowConflict = true }).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Reason.Should().Be("the reference is already taken");
        result.Code.Should().Be("CONFLICT");

        // The main handler ran and the error handler never saw the classified fault.
        calls.Calls.Should().Equal("main");
    }

    [Fact]
    public async Task Classified_main_fault_throws_the_refusal_from_SendAsync()
    {
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<ConflictClassifier>();
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
            },
            new FaultRefusalRecorder(),
            new CompletionRecorder());

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ConflictCommand { ThrowConflict = true }).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Reason.Should().Be("the reference is already taken");
        thrown.Which.Code.Should().Be("CONFLICT");
        thrown.Which.InnerException.Should().BeOfType<ConflictException>();
    }

    [Fact]
    public async Task Classified_main_fault_never_reaches_error_handlers()
    {
        var calls = new FaultRefusalRecorder();
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<ConflictClassifier>();
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictErrorHandler>();
            },
            calls,
            new CompletionRecorder());

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ConflictCommand { ThrowConflict = true }).ConfigureAwait(false);

        // The refusal still raises: skipping the error stage is not recovering.
        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        calls.Calls.Should().NotContain("error");
    }

    [Fact]
    public async Task Classified_main_fault_runs_completion_with_the_refusal_outcome()
    {
        var completions = new CompletionRecorder();
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<ConflictClassifier>();
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictCompletionHandler>();
            },
            new FaultRefusalRecorder(),
            completions);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ConflictCommand { ThrowConflict = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        var observed = completions.Observed.Single().Context;
        observed.Outcome.Should().Be(MediationOutcome.Denied);
        observed.Reason.Should().Be("the reference is already taken");
        observed.Code.Should().Be("CONFLICT");
        observed.Exception.Should().BeOfType<LiteBusMessageDeniedException>();
        observed.Faulted.Should().BeFalse();
    }

    [Fact]
    public async Task Classified_fault_reaches_a_registered_refusal_mapper()
    {
        var calls = new FaultRefusalRecorder();
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<ConflictClassifier>();
            },
            commands =>
            {
                commands.Register<ConflictResultCommand>();
                commands.Register<ConflictResultCommandHandler>();
                commands.Register<ConflictResultMapper>();
            },
            calls,
            new CompletionRecorder());
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var value = await mediator
            .SendAsync(new ConflictResultCommand { ThrowConflict = true }).ConfigureAwait(false);

        value.Should().Be("refused:CONFLICT");
        calls.Calls.Should().Contain("mapper:CONFLICT");

        var tried = await mediator
            .TrySendAsync(new ConflictResultCommand { ThrowConflict = true }).ConfigureAwait(false);

        tried.IsDenied.Should().BeTrue();
        tried.HasValue.Should().BeTrue();
        tried.Value.Should().Be("refused:CONFLICT");
    }

    [Fact]
    public async Task Invalid_classification_returns_Invalid_and_throws_the_invalid_exception()
    {
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<InvalidFaultClassifier>();
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
            },
            new FaultRefusalRecorder(),
            new CompletionRecorder());
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var result = await mediator
            .TrySendAsync(new ConflictCommand { ThrowInvalid = true }).ConfigureAwait(false);

        result.IsInvalid.Should().BeTrue();
        result.Reason.Should().Be("the operation is not acceptable");
        result.Code.Should().Be("NOT_ACCEPTABLE");

        // A classified fault reports one thing wrong rather than validator failures.
        result.Failures.Should().BeEmpty();

        var act = async () => await mediator
            .SendAsync(new ConflictCommand { ThrowInvalid = true }).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageInvalidException>().ConfigureAwait(false);
        thrown.Which.Failures.Should().ContainSingle();
        thrown.Which.Failures[0].Message.Should().Be("the operation is not acceptable");
        thrown.Which.Failures[0].Code.Should().Be("NOT_ACCEPTABLE");
        thrown.Which.InnerException.Should().BeOfType<InvalidFaultException>();
    }

    [Fact]
    public async Task Commit_fault_in_completion_becomes_a_refusal()
    {
        var completions = new CompletionRecorder();
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<ConflictClassifier>();
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictCompletionHandler>();
                commands.Register<ConflictCommitHandler>();
            },
            new FaultRefusalRecorder(),
            completions);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var result = await mediator
            .TrySendAsync(new ConflictCommand { CommitThrows = true }).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.Reason.Should().Be("the reference is already taken");
        result.Code.Should().Be("CONFLICT");

        var act = async () => await mediator
            .SendAsync(new ConflictCommand { CommitThrows = true }).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.InnerException.Should().BeOfType<ConflictException>();

        // The observer ran before the commit and keeps the outcome it saw; only the caller sees the refusal.
        completions.Observed.Select(o => o.Context.Outcome).Should()
            .Equal(MediationOutcome.Succeeded, MediationOutcome.Succeeded);
    }

    [Fact]
    public async Task Without_a_classifier_a_commit_fault_propagates_unchanged()
    {
        var completions = new CompletionRecorder();
        var provider = BuildCommandsProvider(
            null,
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictCompletionHandler>();
                commands.Register<ConflictCommitHandler>();
            },
            new FaultRefusalRecorder(),
            completions);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ConflictCommand { CommitThrows = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<ConflictException>().ConfigureAwait(false);

        var observed = completions.Observed.Single().Context;
        observed.Outcome.Should().Be(MediationOutcome.Succeeded);
    }

    [Fact]
    public async Task Without_a_classifier_a_main_fault_reaches_error_handlers()
    {
        var calls = new FaultRefusalRecorder();
        var completions = new CompletionRecorder();
        var provider = BuildCommandsProvider(
            null,
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictErrorHandler>();
                commands.Register<ConflictCompletionHandler>();
            },
            calls,
            completions);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ConflictCommand { ThrowConflict = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<ConflictException>().ConfigureAwait(false);
        calls.Calls.Should().Equal("main", "error");

        var observed = completions.Observed.Single().Context;
        observed.Outcome.Should().Be(MediationOutcome.Failed);
        observed.Exception.Should().BeOfType<ConflictException>();
    }

    [Fact]
    public async Task Cancellation_is_never_classified()
    {
        var calls = new FaultRefusalRecorder();
        var completions = new CompletionRecorder();
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifierInstance(new RecordingCatchAllClassifier(calls));
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictCompletionHandler>();
            },
            calls,
            completions);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ConflictCommand { ShouldCancel = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        // The catch-all classifier saw nothing: cancellation never reaches one.
        calls.Calls.Should().Equal("main");

        var observed = completions.Observed.Single().Context;
        observed.Outcome.Should().Be(MediationOutcome.Canceled);
    }

    [Fact]
    public async Task First_registered_classifier_wins()
    {
        var firstWinsA = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifierInstance(new FixedCodeClassifier("A"));
                messaging.UseFaultRefusalClassifierInstance(new FixedCodeClassifier("B"));
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
            },
            new FaultRefusalRecorder(),
            new CompletionRecorder());

        var first = await firstWinsA.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new ConflictCommand { ThrowConflict = true }).ConfigureAwait(false);

        first.Code.Should().Be("A");

        var firstWinsB = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifierInstance(new FixedCodeClassifier("B"));
                messaging.UseFaultRefusalClassifierInstance(new FixedCodeClassifier("A"));
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
            },
            new FaultRefusalRecorder(),
            new CompletionRecorder());

        var second = await firstWinsB.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new ConflictCommand { ThrowConflict = true }).ConfigureAwait(false);

        second.Code.Should().Be("B");
    }

    [Fact]
    public async Task Unrecognized_faults_keep_the_failure_path()
    {
        var calls = new FaultRefusalRecorder();
        var completions = new CompletionRecorder();
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<ConflictClassifier>();
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictErrorHandler>();
                commands.Register<ConflictCompletionHandler>();
            },
            calls,
            completions);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ConflictCommand { ThrowUnrecognized = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom").ConfigureAwait(false);
        calls.Calls.Should().Equal("main", "error");

        var observed = completions.Observed.Single().Context;
        observed.Outcome.Should().Be(MediationOutcome.Failed);
    }

    [Fact]
    public async Task Guard_fault_is_classified()
    {
        var calls = new FaultRefusalRecorder();
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<ConflictClassifier>();
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictCommandGuard>();
            },
            calls,
            new CompletionRecorder());

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new ConflictCommand { GuardThrows = true }).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.Code.Should().Be("CONFLICT");

        // The guard threw before the main handler could run.
        calls.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_handler_fault_is_classified()
    {
        var calls = new FaultRefusalRecorder();
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<ConflictClassifier>();
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictCommandPostHandler>();
                commands.Register<ConflictErrorHandler>();
            },
            calls,
            new CompletionRecorder());

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new ConflictCommand { PostThrows = true }).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.Code.Should().Be("CONFLICT");

        // The main handler and the post-handler ran; the error handler never saw the classified fault.
        calls.Calls.Should().Equal("main", "post");
    }

    [Fact]
    public async Task Classified_query_fault_returns_a_refusal_from_TryQueryAsync()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging =>
                {
                    messaging.UseFaultRefusalClassifier<ConflictClassifier>();
                });

                registry.AddQueries(builder =>
                {
                    builder.Register<ConflictQuery>();
                    builder.Register<ConflictQueryHandler>();
                });
            })
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<IQueryMediator>()
            .TryQueryAsync(new ConflictQuery()).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.Reason.Should().Be("the reference is already taken");
        result.Code.Should().Be("CONFLICT");
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task Classified_event_fault_throws_the_refusal_from_PublishAsync()
    {
        var completions = new CompletionRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(completions);

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging =>
                {
                    messaging.UseFaultRefusalClassifier<ConflictClassifier>();
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<ConflictEvent>();
                    builder.Register<ConflictEventHandler>();
                    builder.Register<ConflictEventCompletionHandler>();
                });
            })
            .BuildServiceProvider();

        var act = async () => await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new ConflictEvent()).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be("CONFLICT");
        thrown.Which.InnerException.Should().BeOfType<ConflictException>();

        var observed = completions.Observed.Single().Context;
        observed.Outcome.Should().Be(MediationOutcome.Denied);
    }

    [Fact]
    public async Task Evaluate_reports_a_classified_guard_fault_as_a_denial()
    {
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<ConflictClassifier>();
            },
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictCommandGuard>();
            },
            new FaultRefusalRecorder(),
            new CompletionRecorder());

        var decision = await provider.GetRequiredService<ICommandMediator>()
            .EvaluateAsync(new ConflictCommand { GuardThrows = true }).ConfigureAwait(false);

        decision.IsDenied.Should().BeTrue();
        decision.Reason.Should().Be("the reference is already taken");
        decision.Code.Should().Be("CONFLICT");
    }

    [Fact]
    public async Task Evaluate_without_a_classifier_propagates_the_guard_fault()
    {
        var provider = BuildCommandsProvider(
            null,
            commands =>
            {
                commands.Register<ConflictCommand>();
                commands.Register<ConflictCommandHandler>();
                commands.Register<ConflictCommandGuard>();
            },
            new FaultRefusalRecorder(),
            new CompletionRecorder());

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .EvaluateAsync(new ConflictCommand { GuardThrows = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<ConflictException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task A_classified_fault_is_audited_as_a_denial_not_a_failure()
    {
        var trail = new RecordingAuditTrail();
        var provider = BuildCommandsProvider(
            messaging =>
            {
                messaging.UseFaultRefusalClassifier<ConflictClassifier>();
            },
            commands =>
            {
                commands.Register<AuditedConflictCommand>();
                commands.Register<AuditedConflictCommandHandler>();
                commands.EnableAuditing();
            },
            new FaultRefusalRecorder(),
            new CompletionRecorder(),
            trail);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new AuditedConflictCommand { ThrowConflict = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be("test.conflict-fault");
        record.Outcome.Should().Be(AuditOutcome.Denied);
        record.FailureCode.Should().Be("CONFLICT");
    }
}
