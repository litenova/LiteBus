using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     Pins the stream mediation behavior that a refactor of the strategy would break silently.
/// </summary>
/// <remarks>
///     The stream strategy is the only one that is an iterator, so its faults, its disposal, and its completion timing
///     all hang off enumeration rather than off a return. Those are the parts no other test covers and the parts a
///     restructuring gets wrong without failing loudly.
/// </remarks>
public sealed class StreamMediationParityTests
{
    [Fact]
    public async Task A_stream_yields_the_handler_items_and_completes_once()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, new StageOrderRecorder());
        var query = new SteeredStreamQuery { SourceItems = 3 };

        var items = await provider.GetRequiredService<IQueryMediator>()
            .StreamAsync(query).ToListAsync().ConfigureAwait(true);

        items.Should().Equal(0, 1, 2);
        recorder.Observed.Should().ContainSingle()
            .Which.Context.Outcome.Should().Be(MediationOutcome.Succeeded);
    }

    [Fact]
    public async Task An_override_stream_replaces_the_items_the_caller_receives()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, new StageOrderRecorder());
        var query = new SteeredStreamQuery { SourceItems = 2, ReplaceStream = true, OverrideItems = 2 };

        var items = await provider.GetRequiredService<IQueryMediator>()
            .StreamAsync(query).ToListAsync().ConfigureAwait(true);

        // The source is enumerated first and its items reach the caller, then the override stream follows.
        items.Should().Equal(0, 1, 100, 101);
    }

    [Fact]
    public async Task A_fault_in_the_source_stream_reaches_the_error_handler_and_stops_enumeration()
    {
        var faults = new StageOrderRecorder();
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, faults);
        var query = new SteeredStreamQuery { SourceItems = 2, SourceFaults = true };

        var items = await provider.GetRequiredService<IQueryMediator>()
            .StreamAsync(query).ToListAsync().ConfigureAwait(true);

        // Items produced before the fault still reach the caller; the fault ends enumeration rather than replaying.
        items.Should().Equal(0, 1);
        faults.Observed.Should().ContainSingle().Which.Should().Be("the source stream faulted");
        recorder.Observed.Should().ContainSingle()
            .Which.Context.Outcome.Should().Be(MediationOutcome.Failed);
    }

    [Fact]
    public async Task A_fault_in_the_override_stream_reaches_the_error_handler()
    {
        var faults = new StageOrderRecorder();
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, faults);
        var query = new SteeredStreamQuery
        {
            SourceItems = 1,
            ReplaceStream = true,
            OverrideItems = 1,
            OverrideFaults = true
        };

        var items = await provider.GetRequiredService<IQueryMediator>()
            .StreamAsync(query).ToListAsync().ConfigureAwait(true);

        // The override stream is enumerated by a second copy of the source loop today. This pins that a fault there is
        // routed exactly like a fault in the source.
        items.Should().Equal(0, 100);
        faults.Observed.Should().ContainSingle().Which.Should().Be("the override stream faulted");
        recorder.Observed.Should().ContainSingle()
            .Which.Context.Outcome.Should().Be(MediationOutcome.Failed);
    }

    [Fact]
    public async Task Stopping_enumeration_early_disposes_the_source_and_still_completes()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, new StageOrderRecorder());
        var query = new SteeredStreamQuery { SourceItems = 100 };

        var taken = new List<int>();

        await foreach (var item in provider.GetRequiredService<IQueryMediator>()
                           .StreamAsync(query).ConfigureAwait(true))
        {
            taken.Add(item);

            if (taken.Count == 2)
            {
                break;
            }
        }

        // Completion for a stream fires on enumerator disposal, not on return, so abandoning the stream early must
        // still dispose the handler's enumerator and still produce exactly one completion record.
        taken.Should().Equal(0, 1);
        query.SourceDisposed.Should().BeTrue();
        recorder.Observed.Should().ContainSingle();
    }

    [Fact]
    public async Task The_handler_stream_is_released_before_post_handlers_run()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, new StageOrderRecorder());
        var query = new SteeredStreamQuery { SourceItems = 2 };

        await provider.GetRequiredService<IQueryMediator>()
            .StreamAsync(query).ToListAsync().ConfigureAwait(true);

        // Enumeration of the handler's stream is finished by the time post-handlers run, so its enumerator is released
        // then rather than being held until the whole mediation ends. A post-handler receives the IAsyncEnumerable and
        // would enumerate it afresh, so nothing observes the difference except the resource being held for less time.
        query.SourceDisposedBeforePostHandlers.Should().BeTrue();
    }

    [Fact]
    public async Task A_stream_never_enumerated_produces_no_completion_record()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, new StageOrderRecorder());

        _ = provider.GetRequiredService<IQueryMediator>().StreamAsync(new SteeredStreamQuery());

        await Task.Yield();

        // Inherent to iterators and documented as such: nothing runs until the caller enumerates.
        recorder.Observed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_stream_fault_reaches_the_caller_when_no_error_handler_recovers()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, new StageOrderRecorder(), registerErrorHandler: false);
        var query = new SteeredStreamQuery { SourceItems = 1, SourceFaults = true };

        var act = async () => await provider.GetRequiredService<IQueryMediator>()
            .StreamAsync(query).ToListAsync().ConfigureAwait(true);

        // Same contract as every other strategy: a fault nothing recovered from propagates rather than being
        // swallowed by the enumeration ending quietly.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("the source stream faulted").ConfigureAwait(true);

        recorder.Observed.Should().ContainSingle()
            .Which.Context.Outcome.Should().Be(MediationOutcome.Failed);
    }

    /// <summary>
    ///     Builds a provider for <see cref="SteeredStreamQuery" /> with completion and error observation registered.
    /// </summary>
    /// <param name="recorder">The recorder observing the completion stage.</param>
    /// <param name="faults">The recorder observing the error stage.</param>
    /// <param name="registerErrorHandler">Whether to register the recovering error handler.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        CompletionRecorder recorder,
        StageOrderRecorder faults,
        bool registerErrorHandler = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton(faults);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddQueries(builder =>
                {
                    builder.Register(typeof(SteeredStreamQuery));
                    builder.Register(typeof(SteeredStreamQueryHandler));
                    builder.Register(typeof(SteeredStreamOverridePostHandler));
                    builder.Register(typeof(SteeredStreamCompletionHandler));

                    if (registerErrorHandler)
                    {
                        builder.Register(typeof(SteeredStreamErrorHandler));
                    }
                });
            })
            .BuildServiceProvider();
    }
}
