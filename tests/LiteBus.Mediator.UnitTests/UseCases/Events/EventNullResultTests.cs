using LiteBus.Messaging;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Events;

[Collection("Sequential")]
public sealed class EventNullResultTests : LiteBusTestBase
{
    [Fact]
    public async Task EventPostHandler_ReceivesNullMessageResult()
    {
        var recorder = new NullResultRecorder();
        var provider = BuildProvider(recorder, typeof(NullResultEvent), typeof(NullResultEventHandler), typeof(RecordingEventPostHandler));
        var @event = new NullResultEvent();

        await provider.GetRequiredService<IEventMediator>().PublishAsync(@event).ConfigureAwait(false);

        @event.HandlerRan.Should().BeTrue();
        recorder.PostHandlerRan.Should().BeTrue();
        recorder.SeenResult.Should().BeNull();
    }

    [Fact]
    public async Task EventErrorHandler_ReceivesNullMessageResult()
    {
        var recorder = new NullResultRecorder();
        var provider = BuildProvider(recorder, typeof(FailingResultEvent), typeof(FailingResultEventHandler), typeof(RecordingEventErrorHandler));

        var act = async () => await provider.GetRequiredService<IEventMediator>().PublishAsync(new FailingResultEvent()).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        recorder.ErrorHandlerRan.Should().BeTrue();
        recorder.ErrorSeenResult.Should().BeNull();
    }

    private static ServiceProvider BuildProvider(NullResultRecorder recorder, params Type[] types)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddEvents(builder =>
                {
                    foreach (var type in types)
                    {
                        builder.Register(type);
                    }
                });
            })
            .BuildServiceProvider();
    }
}

public sealed class NullResultRecorder
{
    public bool PostHandlerRan { get; set; }

    public object? SeenResult { get; set; }

    public bool ErrorHandlerRan { get; set; }

    public object? ErrorSeenResult { get; set; }
}

internal sealed class NullResultEvent : IEvent
{
    public bool HandlerRan { get; set; }
}

internal sealed class NullResultEventHandler : IEventHandler<NullResultEvent>
{
    public Task HandleAsync(NullResultEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingEventPostHandler : IEventPostHandler<NullResultEvent>
{
    private readonly NullResultRecorder _recorder;

    public RecordingEventPostHandler(NullResultRecorder recorder)
    {
        _recorder = recorder;
    }

    public Task PostHandleAsync(NullResultEvent message, object? messageResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _recorder.PostHandlerRan = true;
        _recorder.SeenResult = messageResult;
        return Task.CompletedTask;
    }
}

internal sealed class FailingResultEvent : IEvent;

internal sealed class FailingResultEventHandler : IEventHandler<FailingResultEvent>
{
    public Task HandleAsync(FailingResultEvent message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        throw new InvalidOperationException("The handler failed.");
    }
}

internal sealed class RecordingEventErrorHandler : IEventErrorHandler<FailingResultEvent>
{
    private readonly NullResultRecorder _recorder;

    public RecordingEventErrorHandler(NullResultRecorder recorder)
    {
        _recorder = recorder;
    }

    public Task HandleErrorAsync(MessageErrorContext<FailingResultEvent, object> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.ErrorHandlerRan = true;
        _recorder.ErrorSeenResult = context.MessageResult;
        return Task.CompletedTask;
    }
}
