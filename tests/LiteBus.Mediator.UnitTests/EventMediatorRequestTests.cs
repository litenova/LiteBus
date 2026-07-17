using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Events.MediationStrategies;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests;

/// <summary>
///     Verifies event publication maps settings to the core mediation request.
/// </summary>
public sealed class EventMediatorRequestTests
{
    /// <summary>
    ///     Verifies the interface event overload applies default publication settings.
    /// </summary>
    [Fact]
    public async Task PublishAsync_InterfaceEvent_ShouldCreateDefaultRequest()
    {
        var messageMediator = new RecordingMessageMediator();
        var mediator = new EventMediator(messageMediator);
        IEvent @event = new TestEvent();

        await mediator.PublishAsync(@event).ConfigureAwait(false);

        var request = messageMediator.Request.Should()
            .BeOfType<MessageMediationRequest<IEvent, Task>>().Subject;
        request.MessageMediationStrategy.Should().BeOfType<AsyncBroadcastMediationStrategy<IEvent>>();
        request.RegisterPlainMessagesOnSpot.Should().BeFalse();
        messageMediator.Message.Should().BeSameAs(@event);
    }

    /// <summary>
    ///     Verifies the generic event overload forwards routing and contextual settings.
    /// </summary>
    [Fact]
    public async Task PublishAsync_GenericEvent_ShouldForwardSettings()
    {
        var messageMediator = new RecordingMessageMediator();
        var mediator = new EventMediator(messageMediator);
        var @event = new PlainEvent();
        using var cancellation = new CancellationTokenSource();
        string[] tags = ["shipping", "audit"];
        var items = new Dictionary<string, object> { ["tenant"] = "north" };
        var settings = new EventMediationSettings
        {
            AutoRegisterUnregisteredMessageTypes = true,
            Items = items,
            Routing = new EventRoutingSettings
            {
                Tags = tags,
                HandlerPredicate = descriptor => descriptor.Tags.Contains("shipping")
            }
        };

        await mediator.PublishAsync(@event, settings, cancellation.Token).ConfigureAwait(false);

        var request = messageMediator.Request.Should()
            .BeOfType<MessageMediationRequest<PlainEvent, Task>>().Subject;
        request.MessageMediationStrategy.Should().BeOfType<AsyncBroadcastMediationStrategy<PlainEvent>>();
        request.Tags.Should().BeSameAs(tags);
        request.Items.Should().BeSameAs(items);
        request.RegisterPlainMessagesOnSpot.Should().BeTrue();
        messageMediator.CancellationToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    ///     Verifies both publication overloads reject null events before mediation.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WithNullEvent_ShouldThrow()
    {
        var mediator = new EventMediator(new RecordingMessageMediator());

        var interfaceAct = () => mediator.PublishAsync((IEvent)null!);
        var genericAct = () => mediator.PublishAsync<PlainEvent>(null!);

        await interfaceAct.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        await genericAct.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }

    private sealed class RecordingMessageMediator : IMessageMediator
    {
        public object? Message { get; private set; }

        public object? Request { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public TMessageResult Mediate<TMessage, TMessageResult>(
            TMessage message,
            MessageMediationRequest<TMessage, TMessageResult> request,
            CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            Message = message;
            Request = request;
            CancellationToken = cancellationToken;
            return (TMessageResult)(object)Task.CompletedTask;
        }

    }

    private sealed class TestEvent : IEvent;

    private sealed class PlainEvent;
}
