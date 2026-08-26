using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     Verifies that the event axis has the same two stages the command and query axes have: a gate that can stop the
///     broadcast, and a completion handler that observes how it ended.
/// </summary>
[Collection("Sequential")]
public sealed class EventGateAndCompletionTests : LiteBusTestBase
{
    [Fact]
    public async Task An_event_gate_can_skip_the_reactions_to_an_already_handled_event()
    {
        var observed = new List<MessageOutcome>();
        var provider = BuildProvider(observed);
        var @event = new ProbeEvent { AlreadyHandled = true };

        await provider.GetRequiredService<IEventMediator>().PublishAsync(@event).ConfigureAwait(false);

        @event.HandlerRan.Should().BeFalse();
        observed.Should().Equal(MessageOutcome.ShortCircuited);
    }

    [Fact]
    public async Task An_event_completion_handler_observes_a_successful_broadcast()
    {
        var observed = new List<MessageOutcome>();
        var provider = BuildProvider(observed);
        var @event = new ProbeEvent();

        await provider.GetRequiredService<IEventMediator>().PublishAsync(@event).ConfigureAwait(false);

        @event.HandlerRan.Should().BeTrue();
        observed.Should().Equal(MessageOutcome.Succeeded);
    }

    /// <summary>
    ///     Builds a provider registering the event probe types.
    /// </summary>
    /// <param name="observed">The list the completion handler appends outcomes to.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(List<MessageOutcome> observed)
    {
        var services = new ServiceCollection();
        services.AddSingleton(observed);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddEvents(builder =>
                {
                    builder.Register(typeof(ProbeEvent));
                    builder.Register(typeof(ProbeEventHandler));
                    builder.Register(typeof(ProbeEventGate));
                    builder.Register(typeof(ProbeEventCompletionHandler));
                });
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     An event whose broadcast the test steers.
/// </summary>
internal sealed class ProbeEvent : IEvent
{
    /// <summary>
    ///     Gets or sets a value indicating whether the gate treats the event as already handled.
    /// </summary>
    public bool AlreadyHandled { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the event handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Skips the reactions to an event this process has already handled.
/// </summary>
internal sealed class ProbeEventGate : IEventGate<ProbeEvent>
{
    /// <inheritdoc />
    public Task<PipelineDirective> DecideAsync(ProbeEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.AlreadyHandled
            ? PipelineDirective.ShortCircuit("already handled in this process")
            : PipelineDirective.Continue);
    }
}

/// <summary>
///     Records that the event reached a handler.
/// </summary>
internal sealed class ProbeEventHandler : IEventHandler<ProbeEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(ProbeEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records how the broadcast ended.
/// </summary>
internal sealed class ProbeEventCompletionHandler : IEventCompletionHandler<ProbeEvent>
{
    /// <summary>
    ///     The outcomes observed by the test.
    /// </summary>
    private readonly List<MessageOutcome> _observed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProbeEventCompletionHandler" /> class.
    /// </summary>
    /// <param name="observed">The list to append outcomes to.</param>
    public ProbeEventCompletionHandler(List<MessageOutcome> observed)
    {
        _observed = observed;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<ProbeEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _observed.Add(context.Outcome);

        // An event produces no result, so the completion context carries none rather than the task that tracked its
        // handlers.
        context.MessageResult.Should().BeNull();
        return Task.CompletedTask;
    }
}
