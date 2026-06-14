using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

public sealed class MessageResolveStrategyTests
{
    [Fact]
    public void Find_WhenMidTypeAndBaseTypeAreRegistered_ShouldPreferMostDerivedAssignableDescriptor()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(BaseEventHandler));
        registry.Register(typeof(MidEventHandler));

        var strategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();
        var descriptor = strategy.Find(typeof(LeafEvent), registry);

        descriptor.Should().NotBeNull();
        descriptor.MessageType.Should().Be(typeof(MidEvent));
    }

    [Fact]
    public void Find_WhenMultipleAssignableDescriptorsShareDepth_ShouldThrowAmbiguousMessageResolveException()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(InterfaceAlphaHandler));
        registry.Register(typeof(InterfaceBetaHandler));

        var strategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        var act = () => strategy.Find(typeof(DualInterfaceMessage), registry);

        act.Should().Throw<AmbiguousMessageResolveException>();
    }

    private class BaseEvent : IEvent
    {
    }

    private class MidEvent : BaseEvent
    {
    }

    private class LeafEvent : MidEvent
    {
    }

    private interface IAlphaMarker;

    private interface IBetaMarker;

    private sealed class DualInterfaceMessage : IAlphaMarker, IBetaMarker;

    private sealed class BaseEventHandler : IEventHandler<BaseEvent>
    {
        public Task HandleAsync(BaseEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class MidEventHandler : IEventHandler<MidEvent>
    {
        public Task HandleAsync(MidEvent @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class InterfaceAlphaHandler : IEventHandler<IAlphaMarker>
    {
        public Task HandleAsync(IAlphaMarker @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class InterfaceBetaHandler : IEventHandler<IBetaMarker>
    {
        public Task HandleAsync(IBetaMarker @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
