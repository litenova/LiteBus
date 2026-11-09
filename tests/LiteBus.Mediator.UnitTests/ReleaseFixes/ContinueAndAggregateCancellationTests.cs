using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes.ContinueAndAggregateCancellation;

/// <summary>
///     Verifies that under <see cref="HandlerFaultMode.ContinueAndAggregate" /> a cancellation a sequential event handler
///     raises itself is that handler's failure, while cancelling the publish still stops the group.
/// </summary>
/// <remarks>
///     An <see cref="OperationCanceledException" /> from a handler, such as an HTTP client timeout, used to stop the
///     rest of the priority group, so the subscribers after it silently never ran.
/// </remarks>
[Collection("Sequential")]
public sealed class ContinueAndAggregateCancellationTests : LiteBusTestBase
{
    [Fact]
    public async Task A_handler_timeout_does_not_stop_the_rest_of_its_group()
    {
        var recorder = new CancellationRecorder();
        var provider = BuildProvider(recorder, builder =>
        {
            builder.Register<TimingOutHandler>();
            builder.Register<SecondHandler>();
        });

        var act = () => provider.GetRequiredService<IEventMediator>().PublishAsync(new StockChangedEvent(), Aggregate());

        // Every collected failure is a cancellation, so the publish reports cancellation rather than a fault.
        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        recorder.Entries.Should().Equal("timed-out", "second");
    }

    [Fact]
    public async Task A_handler_timeout_and_a_fault_aggregate()
    {
        var recorder = new CancellationRecorder();
        var provider = BuildProvider(recorder, builder =>
        {
            builder.Register<TimingOutHandler>();
            builder.Register<FaultingHandler>();
            builder.Register<SecondHandler>();
        });

        var act = () => provider.GetRequiredService<IEventMediator>().PublishAsync(new StockChangedEvent(), Aggregate());

        var thrown = await act.Should().ThrowAsync<AggregateException>().ConfigureAwait(false);
        thrown.Which.InnerExceptions.Should().HaveCount(2);
        recorder.Entries.Should().Equal("timed-out", "faulted", "second");
    }

    [Fact]
    public async Task Cancelling_the_publish_stops_the_group()
    {
        var recorder = new CancellationRecorder();
        using var publish = new CancellationTokenSource();
        var provider = BuildProvider(recorder, builder =>
        {
            builder.Register<CancellingPublishHandler>();
            builder.Register<SecondHandler>();
        });
        recorder.Publish = publish;

        var act = () => provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new StockChangedEvent(), Aggregate(), publish.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        recorder.Entries.Should().Equal("cancelled-publish");
    }

    /// <summary>
    ///     Creates settings that run the group sequentially and aggregate failures.
    /// </summary>
    /// <returns>The publish settings.</returns>
    private static EventMediationSettings Aggregate()
    {
        return new EventMediationSettings
        {
            Execution = new EventExecutionSettings { FaultMode = HandlerFaultMode.ContinueAndAggregate }
        };
    }

    /// <summary>
    ///     Builds a provider with the messaging and event modules.
    /// </summary>
    /// <param name="recorder">The recorder the handlers write to.</param>
    /// <param name="events">Registers the event handlers under test, in the order they run.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(CancellationRecorder recorder, Action<EventModuleBuilder> events)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(liteBus =>
            {
                liteBus.AddMessaging(_ => { });
                liteBus.AddEvents(builder =>
                {
                    builder.Register<StockChangedEvent>();
                    events(builder);
                });
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     Records which handlers ran, in order.
/// </summary>
internal sealed class CancellationRecorder
{
    /// <summary>
    ///     Holds the recorded entries.
    /// </summary>
    private readonly List<string> _entries = [];

    /// <summary>
    ///     Gets the recorded entries in the order they were written.
    /// </summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>
    ///     Gets or sets the source of the publish token, which a handler cancels to stop the publish.
    /// </summary>
    public CancellationTokenSource? Publish { get; set; }

    /// <summary>
    ///     Records one entry.
    /// </summary>
    /// <param name="entry">The entry to record.</param>
    public void Record(string entry)
    {
        _entries.Add(entry);
    }
}

/// <summary>
///     The event every handler in this file subscribes to.
/// </summary>
internal sealed class StockChangedEvent : IEvent;

/// <summary>
///     Fails the way an HTTP client timeout does, while the publish token is not cancelled.
/// </summary>
internal sealed class TimingOutHandler : IEventHandler<StockChangedEvent>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CancellationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TimingOutHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public TimingOutHandler(CancellationRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(StockChangedEvent message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("timed-out");
        throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
    }
}

/// <summary>
///     Fails with an ordinary fault.
/// </summary>
internal sealed class FaultingHandler : IEventHandler<StockChangedEvent>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CancellationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FaultingHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public FaultingHandler(CancellationRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(StockChangedEvent message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("faulted");
        throw new InvalidOperationException("Stock projection failed.");
    }
}

/// <summary>
///     Cancels the publish token and observes it, as a host shutting down would.
/// </summary>
internal sealed class CancellingPublishHandler : IEventHandler<StockChangedEvent>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CancellationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CancellingPublishHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public CancellingPublishHandler(CancellationRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public async Task HandleAsync(StockChangedEvent message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("cancelled-publish");
        await _recorder.Publish!.CancelAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}

/// <summary>
///     Records that it ran.
/// </summary>
internal sealed class SecondHandler : IEventHandler<StockChangedEvent>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CancellationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SecondHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SecondHandler(CancellationRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(StockChangedEvent message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("second");
        return Task.CompletedTask;
    }
}
