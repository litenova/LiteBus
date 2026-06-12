using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;

namespace LiteBus.MessageModule.UnitTests;

public sealed class MessageResolveStrategyTests
{
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

    private interface IAlphaMarker;

    private interface IBetaMarker;

    private sealed class DualInterfaceMessage : IAlphaMarker, IBetaMarker;

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
