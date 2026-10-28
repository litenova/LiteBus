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
///     Verifies that <c>UseUnitOfWork</c> commits once per successful query mediation and never otherwise.
/// </summary>
[Collection("Sequential")]
public sealed class UnitOfWorkQueryTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider registering only the unit-of-work query test types, keeping other axes untouched.
    /// </summary>
    /// <typeparam name="TUnitOfWork">The unit of work implementation the query module commits.</typeparam>
    /// <param name="recorder">The recorder shared with the unit of work and the completion probes.</param>
    /// <param name="completionProbes">The completion probe types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider<TUnitOfWork>(QueryUnitOfWorkRecorder recorder, params Type[] completionProbes)
        where TUnitOfWork : class, IUnitOfWork
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddQueries(builder =>
                {
                    builder.Register<UnitOfWorkQuery>();
                    builder.Register<UnitOfWorkQueryHandler>();
                    builder.Register<UnitOfWorkQueryGuard>();
                    builder.Register<UnitOfWorkQueryValidator>();
                    builder.Register<UnitOfWorkQueryShortcut>();

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
    public async Task Commit_runs_once_after_a_successful_query()
    {
        var recorder = new QueryUnitOfWorkRecorder();
        var provider = BuildProvider<RecordingQueryUnitOfWork>(recorder);

        var result = await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new UnitOfWorkQuery()).ConfigureAwait(false);

        result.Should().Be("produced");
        recorder.Events.Should().Equal("commit");
    }

    [Fact]
    public async Task Commit_does_not_run_after_a_query_guard_denial()
    {
        var recorder = new QueryUnitOfWorkRecorder();
        var provider = BuildProvider<RecordingQueryUnitOfWork>(recorder);

        var act = async () => await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new UnitOfWorkQuery { ShouldDeny = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Commit_does_not_run_after_a_query_validation_failure()
    {
        var recorder = new QueryUnitOfWorkRecorder();
        var provider = BuildProvider<RecordingQueryUnitOfWork>(recorder);

        var act = async () => await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new UnitOfWorkQuery { ShouldBeInvalid = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageInvalidException>().ConfigureAwait(false);
        recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Commit_does_not_run_after_a_query_handler_exception()
    {
        var recorder = new QueryUnitOfWorkRecorder();
        var provider = BuildProvider<RecordingQueryUnitOfWork>(recorder);

        var act = async () => await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new UnitOfWorkQuery { ShouldThrow = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Commit_does_not_run_when_a_query_shortcut_answers()
    {
        var recorder = new QueryUnitOfWorkRecorder();
        var provider = BuildProvider<RecordingQueryUnitOfWork>(recorder);

        var result = await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new UnitOfWorkQuery { ShouldAnswer = true }).ConfigureAwait(false);

        // The main handler never ran, so there is nothing to commit, even though an answer reports success.
        result.Should().Be("cached");
        recorder.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Commit_runs_between_lower_and_higher_priority_query_completion_handlers()
    {
        var recorder = new QueryUnitOfWorkRecorder();
        var provider = BuildProvider<RecordingQueryUnitOfWork>(
            recorder,
            typeof(EarlyQueryCompletionProbe),
            typeof(LateQueryCompletionProbe));

        await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new UnitOfWorkQuery()).ConfigureAwait(false);

        recorder.Events.Should().Equal("early", "commit", "late");
    }

    [Fact]
    public async Task A_throwing_commit_fails_QueryAsync_with_that_exception()
    {
        var recorder = new QueryUnitOfWorkRecorder();
        var provider = BuildProvider<ThrowingQueryUnitOfWork>(recorder);

        var act = async () => await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new UnitOfWorkQuery()).ConfigureAwait(false);

        // The completion stage suppresses a handler fault only when the mediation had already failed, so a commit
        // failure after a success propagates rather than silently losing the write.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("commit failed").ConfigureAwait(false);
    }

    [Fact]
    public void Calling_UseUnitOfWork_twice_on_the_query_builder_throws()
    {
        var act = () =>
        {
            new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddQueries(builder =>
                {
                    builder.Register<UnitOfWorkQuery>();
                    builder.Register<UnitOfWorkQueryHandler>();
                    builder.UseUnitOfWork<RecordingQueryUnitOfWork>();
                    builder.UseUnitOfWork<ThrowingQueryUnitOfWork>();
                });
            });
        };

        act.Should().Throw<LiteBusConfigurationException>();
    }
}
