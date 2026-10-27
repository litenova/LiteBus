using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UnitOfWork;

/// <summary>
///     Verifies that <c>UseUnitOfWork</c> commits once per successful command mediation and never otherwise.
/// </summary>
[Collection("Sequential")]
public sealed class UnitOfWorkTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider registering only the unit-of-work command test types, keeping other axes untouched.
    /// </summary>
    /// <typeparam name="TUnitOfWork">The unit of work implementation the command module commits.</typeparam>
    /// <param name="recorder">The recorder shared with the unit of work and the completion probes.</param>
    /// <param name="completionProbes">The completion probe types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider<TUnitOfWork>(UnitOfWorkRecorder recorder, params Type[] completionProbes)
        where TUnitOfWork : class, IUnitOfWork
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<UnitOfWorkCommand>();
                    builder.Register<UnitOfWorkCommandHandler>();
                    builder.Register<UnitOfWorkCommandGuard>();
                    builder.Register<UnitOfWorkCommandValidator>();
                    builder.Register<UnitOfWorkCommandShortcut>();

                    foreach (var probe in completionProbes)
                    {
                        builder.Register(probe);
                    }

                    builder.UseUnitOfWork<TUnitOfWork>();
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Commit_runs_once_after_a_successful_command()
    {
        var recorder = new UnitOfWorkRecorder();
        var provider = BuildProvider<RecordingUnitOfWork>(recorder);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new UnitOfWorkCommand()).ConfigureAwait(false);

        recorder.Events.Should().Equal("commit");
    }

    [Fact]
    public async Task Commit_does_not_run_after_a_guard_denial()
    {
        var recorder = new UnitOfWorkRecorder();
        var provider = BuildProvider<RecordingUnitOfWork>(recorder);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new UnitOfWorkCommand { ShouldDeny = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Commit_does_not_run_after_a_validation_failure()
    {
        var recorder = new UnitOfWorkRecorder();
        var provider = BuildProvider<RecordingUnitOfWork>(recorder);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new UnitOfWorkCommand { ShouldBeInvalid = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageInvalidException>().ConfigureAwait(false);
        recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Commit_does_not_run_after_a_handler_exception()
    {
        var recorder = new UnitOfWorkRecorder();
        var provider = BuildProvider<RecordingUnitOfWork>(recorder);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new UnitOfWorkCommand { ShouldThrow = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Commit_does_not_run_when_a_shortcut_answers()
    {
        var recorder = new UnitOfWorkRecorder();
        var provider = BuildProvider<RecordingUnitOfWork>(recorder);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new UnitOfWorkCommand { ShouldAnswer = true }).ConfigureAwait(false);

        // The main handler never ran, so there is nothing to commit, even though an answer reports success.
        recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Commit_runs_between_lower_and_higher_priority_completion_handlers()
    {
        var recorder = new UnitOfWorkRecorder();
        var provider = BuildProvider<RecordingUnitOfWork>(
            recorder,
            typeof(EarlyCommandCompletionProbe),
            typeof(LateCommandCompletionProbe));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new UnitOfWorkCommand()).ConfigureAwait(false);

        recorder.Events.Should().Equal("early", "commit", "late");
    }

    [Fact]
    public async Task A_throwing_commit_fails_SendAsync_with_that_exception()
    {
        var recorder = new UnitOfWorkRecorder();
        var provider = BuildProvider<ThrowingUnitOfWork>(recorder);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new UnitOfWorkCommand()).ConfigureAwait(false);

        // The completion stage suppresses a handler fault only when the mediation had already failed, so a commit
        // failure after a success propagates rather than silently losing the write.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("commit failed").ConfigureAwait(false);
    }

    [Fact]
    public void Calling_UseUnitOfWork_twice_on_the_command_builder_throws()
    {
        var act = () =>
        {
            new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<UnitOfWorkCommand>();
                    builder.Register<UnitOfWorkCommandHandler>();
                    builder.UseUnitOfWork<RecordingUnitOfWork>();
                    builder.UseUnitOfWork<ThrowingUnitOfWork>();
                });
            });
        };

        act.Should().Throw<LiteBusConfigurationException>();
    }

    [Fact]
    public async Task Registering_different_unit_of_work_types_on_commands_and_queries_is_allowed()
    {
        var commandRecorder = new UnitOfWorkRecorder();
        var queryRecorder = new QueryUnitOfWorkRecorder();

        var provider = new ServiceCollection()
            .AddSingleton(commandRecorder)
            .AddSingleton(queryRecorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<UnitOfWorkCommand>();
                    builder.Register<UnitOfWorkCommandHandler>();
                    builder.UseUnitOfWork<RecordingUnitOfWork>();
                });

                registry.AddQueries(builder =>
                {
                    builder.Register<UnitOfWorkQuery>();
                    builder.Register<UnitOfWorkQueryHandler>();
                    builder.UseUnitOfWork<RecordingQueryUnitOfWork>();
                });
            })
            .BuildServiceProvider();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new UnitOfWorkCommand()).ConfigureAwait(false);

        // Each axis commits its own unit of work: the command left the query recorder untouched.
        commandRecorder.Events.Should().Equal("commit");
        queryRecorder.Events.Should().BeEmpty();

        var result = await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new UnitOfWorkQuery()).ConfigureAwait(false);

        result.Should().Be("produced");
        queryRecorder.Events.Should().Equal("commit");
        commandRecorder.Events.Should().Equal("commit");
    }
}
