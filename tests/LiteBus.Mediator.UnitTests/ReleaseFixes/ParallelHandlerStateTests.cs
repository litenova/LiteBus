using System.Collections.Concurrent;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies that event handlers running in parallel over one mediation share execution-context state and audit
///     scope state without corruption or lost writes.
/// </summary>
[Collection("Sequential")]
public sealed class ParallelHandlerStateTests : LiteBusTestBase
{
    /// <summary>
    ///     The writes each handler performs. High enough that a torn dictionary fails loudly rather than by luck.
    /// </summary>
    internal const int WritesPerHandler = 300;

    [Fact]
    public async Task Parallel_handlers_share_execution_context_state_without_corruption()
    {
        var capture = new ParallelWriteCapture();
        var services = new ServiceCollection();
        services.AddSingleton(capture);

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddEvents(builder =>
                {
                    builder.Register<ParallelWriteEvent>();
                    builder.Register<ParallelWriteEventHandlerA>();
                    builder.Register<ParallelWriteEventHandlerB>();
                    builder.Register<ParallelWriteCompletionHandler>();
                });
            })
            .BuildServiceProvider();

        var settings = new EventMediationSettings
        {
            Execution = new EventExecutionSettings
            {
                HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel
            }
        };

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new ParallelWriteEvent(), settings).ConfigureAwait(false);

        // Every write used a distinct key, so a missing one is a lost update and an exception above is a torn
        // dictionary. Framework keys may sit beside these, so each side asserts containment rather than equality.
        for (var i = 0; i < WritesPerHandler; i++)
        {
            capture.ItemKeys.Should().Contain($"handler-a-{i}");
            capture.ItemKeys.Should().Contain($"handler-b-{i}");
            capture.PropertyNames.Should().Contain($"prop-a-{i}");
            capture.PropertyNames.Should().Contain($"prop-b-{i}");
        }
    }
}

/// <summary>
///     Snapshots the keys two parallel handlers wrote, taken once both finished.
/// </summary>
internal sealed class ParallelWriteCapture
{
    /// <summary>
    ///     Gets the execution-context item keys observed at completion.
    /// </summary>
    public ConcurrentQueue<string> ItemKeys { get; } = new();

    /// <summary>
    ///     Gets the audit property names observed at completion.
    /// </summary>
    public ConcurrentQueue<string> PropertyNames { get; } = new();
}

/// <summary>
///     An event whose handlers write shared state concurrently.
/// </summary>
internal sealed class ParallelWriteEvent : IEvent;

/// <summary>
///     Writes one family of execution-context items and audit properties, yielding so the sibling interleaves.
/// </summary>
internal sealed class ParallelWriteEventHandlerA : IEventHandler<ParallelWriteEvent>
{
    /// <summary>
    ///     The ambient audit scope.
    /// </summary>
    private readonly IAuditScope _audit;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ParallelWriteEventHandlerA" /> class.
    /// </summary>
    /// <param name="audit">The ambient audit scope.</param>
    public ParallelWriteEventHandlerA(IAuditScope audit)
    {
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ParallelWriteEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        for (var i = 0; i < ParallelHandlerStateTests.WritesPerHandler; i++)
        {
            AmbientExecutionContext.Current.Items[$"handler-a-{i}"] = i;
            _audit.WithProperty($"prop-a-{i}", "value");
            await Task.Yield();
        }
    }
}

/// <summary>
///     Writes a second family of execution-context items and audit properties, yielding so the sibling interleaves.
/// </summary>
internal sealed class ParallelWriteEventHandlerB : IEventHandler<ParallelWriteEvent>
{
    /// <summary>
    ///     The ambient audit scope.
    /// </summary>
    private readonly IAuditScope _audit;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ParallelWriteEventHandlerB" /> class.
    /// </summary>
    /// <param name="audit">The ambient audit scope.</param>
    public ParallelWriteEventHandlerB(IAuditScope audit)
    {
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ParallelWriteEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        for (var i = 0; i < ParallelHandlerStateTests.WritesPerHandler; i++)
        {
            AmbientExecutionContext.Current.Items[$"handler-b-{i}"] = i;
            _audit.WithProperty($"prop-b-{i}", "value");
            await Task.Yield();
        }
    }
}

/// <summary>
///     Snapshots the shared keys once both handlers finished.
/// </summary>
internal sealed class ParallelWriteCompletionHandler : IEventCompletionHandler<ParallelWriteEvent>
{
    /// <summary>
    ///     The ambient audit scope.
    /// </summary>
    private readonly IAuditScope _audit;

    /// <summary>
    ///     The capture shared with the test.
    /// </summary>
    private readonly ParallelWriteCapture _capture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ParallelWriteCompletionHandler" /> class.
    /// </summary>
    /// <param name="audit">The ambient audit scope.</param>
    /// <param name="capture">The capture shared with the test.</param>
    public ParallelWriteCompletionHandler(IAuditScope audit, ParallelWriteCapture capture)
    {
        _audit = audit;
        _capture = capture;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(
        MessageCompletionContext<ParallelWriteEvent> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var key in AmbientExecutionContext.Current.Items.Keys)
        {
            _capture.ItemKeys.Enqueue(key);
        }

        foreach (var name in _audit.Properties.Keys)
        {
            _capture.PropertyNames.Enqueue(name);
        }

        return Task.CompletedTask;
    }
}
