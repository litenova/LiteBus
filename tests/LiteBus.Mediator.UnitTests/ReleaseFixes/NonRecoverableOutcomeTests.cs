using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies that a fault the recoverable filter excludes is still recorded before completion runs, so completion
///     never observes <see cref="MediationOutcome.Succeeded" /> for a mediation that threw.
/// </summary>
[Collection("Sequential")]
public sealed class NonRecoverableOutcomeTests : LiteBusTestBase
{
    [Fact]
    public async Task Void_command_with_no_handler_completes_Failed_with_the_resolution_fault()
    {
        var recorder = new CompletionRecorder();
        var mediator = BuildCommandsProvider(recorder).GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new UnhandledVoidCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<NoHandlerFoundException>().ConfigureAwait(false);

        var observed = recorder.Observed.Should().ContainSingle().Subject.Context;
        observed.Outcome.Should().Be(MediationOutcome.Failed);
        observed.Exception.Should().BeOfType<NoHandlerFoundException>();
    }

    [Fact]
    public async Task Result_command_with_no_handler_completes_Failed_with_the_resolution_fault()
    {
        var recorder = new CompletionRecorder();
        var mediator = BuildCommandsProvider(recorder).GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new UnhandledResultCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<NoHandlerFoundException>().ConfigureAwait(false);

        var observed = recorder.Observed.Should().ContainSingle().Subject.Context;
        observed.Outcome.Should().Be(MediationOutcome.Failed);
        observed.Exception.Should().BeOfType<NoHandlerFoundException>();
    }

    [Fact]
    public async Task Broadcast_with_no_handler_and_ThrowIfNoHandlerFound_completes_Failed()
    {
        var recorder = new CompletionRecorder();
        var mediator = BuildEventsProvider(recorder).GetRequiredService<IEventMediator>();
        var settings = new EventMediationSettings { ThrowIfNoHandlerFound = true };

        var act = async () => await mediator.PublishAsync(new UnhandledEvent(), settings).ConfigureAwait(false);

        await act.Should().ThrowAsync<NoHandlerFoundException>().ConfigureAwait(false);

        var observed = recorder.Observed.Should().ContainSingle().Subject.Context;
        observed.Outcome.Should().Be(MediationOutcome.Failed);
        observed.Exception.Should().BeOfType<NoHandlerFoundException>();
    }

    [Fact]
    public async Task Stream_query_with_no_handler_completes_Failed_with_the_resolution_fault()
    {
        var recorder = new CompletionRecorder();
        var mediator = BuildQueriesProvider(recorder).GetRequiredService<IQueryMediator>();
        var items = new List<int>();

        var act = async () =>
        {
            await foreach (var item in mediator.StreamAsync(new UnhandledStreamQuery()).ConfigureAwait(false))
            {
                items.Add(item);
            }
        };

        await act.Should().ThrowAsync<NoHandlerFoundException>().ConfigureAwait(false);

        items.Should().BeEmpty();

        var observed = recorder.Observed.Should().ContainSingle().Subject.Context;
        observed.Outcome.Should().Be(MediationOutcome.Failed);
        observed.Exception.Should().BeOfType<NoHandlerFoundException>();
    }

    [Fact]
    public async Task Canceled_stream_enumeration_completes_Canceled()
    {
        var recorder = new CompletionRecorder();
        var mediator = BuildQueriesProvider(recorder).GetRequiredService<IQueryMediator>();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var items = new List<int>();

        var act = async () =>
        {
            await foreach (var item in mediator
                               .StreamAsync(new CancelProbeStreamQuery(), cancellationToken: canceled.Token)
                               .ConfigureAwait(false))
            {
                items.Add(item);
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var observed = recorder.Observed.Should().ContainSingle().Subject.Context;
        observed.Outcome.Should().Be(MediationOutcome.Canceled);
        observed.Exception.Should().BeAssignableTo<OperationCanceledException>();
    }

    /// <summary>
    ///     Builds a provider registering message types with no main handlers, so resolution fails inside the strategy.
    /// </summary>
    /// <param name="recorder">The recorder shared with the completion handler.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildCommandsProvider(CompletionRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<UnhandledVoidCommand>();
                    builder.Register<UnhandledResultCommand>();
                    builder.Register<RecordingCommandCompletionHandler>();
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider registering an event with no subscribers.
    /// </summary>
    /// <param name="recorder">The recorder shared with the completion handler.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildEventsProvider(CompletionRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddEvents(builder =>
                {
                    builder.Register<UnhandledEvent>();
                    builder.Register<RecordingEventCompletionHandler>();
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider registering one stream query with no handler and one with a handler.
    /// </summary>
    /// <param name="recorder">The recorder shared with the completion handler.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildQueriesProvider(CompletionRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddQueries(builder =>
                {
                    builder.Register<UnhandledStreamQuery>();
                    builder.Register<CancelProbeStreamQuery>();
                    builder.Register<CancelProbeStreamHandler>();
                    builder.Register<RecordingQueryCompletionHandler>();
                });
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     A command with no main handler, so resolution fails inside the strategy rather than before it.
/// </summary>
internal sealed class UnhandledVoidCommand : ICommand;

/// <summary>
///     A result command with no main handler, so resolution fails inside the strategy rather than before it.
/// </summary>
internal sealed class UnhandledResultCommand : ICommand<string>;

/// <summary>
///     An event with no subscribers.
/// </summary>
internal sealed class UnhandledEvent : IEvent;

/// <summary>
///     A stream query with no handler.
/// </summary>
internal sealed class UnhandledStreamQuery : IStreamQuery<int>;

/// <summary>
///     A stream query whose enumeration the test cancels.
/// </summary>
internal sealed class CancelProbeStreamQuery : IStreamQuery<int>;

/// <summary>
///     Streams three items, so a canceled enumeration has something to abandon.
/// </summary>
internal sealed class CancelProbeStreamHandler : IStreamQueryHandler<CancelProbeStreamQuery, int>
{
    /// <inheritdoc />
    public IAsyncEnumerable<int> StreamAsync(CancelProbeStreamQuery message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return YieldItemsAsync();
    }

    /// <summary>
    ///     Yields the items the canceled enumeration abandons.
    /// </summary>
    /// <returns>Three items.</returns>
    private static async IAsyncEnumerable<int> YieldItemsAsync()
    {
        for (var i = 0; i < 3; i++)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return i;
        }
    }
}

/// <summary>
///     Records how a command mediation ended.
/// </summary>
internal sealed class RecordingCommandCompletionHandler : ICommandCompletionHandler
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordingCommandCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public RecordingCommandCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<ICommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("command", context.AsUntyped()));
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records how an event broadcast ended.
/// </summary>
internal sealed class RecordingEventCompletionHandler : IEventCompletionHandler
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordingEventCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public RecordingEventCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<IEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("event", context.AsUntyped()));
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records how a query mediation ended.
/// </summary>
internal sealed class RecordingQueryCompletionHandler : IQueryCompletionHandler
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordingQueryCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public RecordingQueryCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<IQuery> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("query", context.AsUntyped()));
        return Task.CompletedTask;
    }
}
