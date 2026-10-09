using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

public sealed class ActualTypeOrFirstAssignableTypeMessageResolveStrategyDepthTests
{
    [Fact]
    public void Find_WhenTwoRegisteredBaseInterfacesTie_ShouldThrowWithCandidates()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(AlphaMarkerHandler));
        registry.Register(typeof(BetaMarkerHandler));

        var strategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();
        var act = () => strategy.Find(typeof(DualMarkerMessage), registry);

        var exception = act.Should().Throw<AmbiguousMessageResolveException>().Which;
        exception.Candidates.Should().HaveCount(2);
        exception.Candidates.Should().Contain(typeof(IAlphaMarker));
        exception.Candidates.Should().Contain(typeof(IBetaMarker));
        exception.Message.Should().Contain(typeof(IAlphaMarker).FullName!);
        exception.Message.Should().Contain(typeof(IBetaMarker).FullName!);
    }

    [Fact]
    public void Find_WhenOneRegisteredInterfaceImplementsThree_ShouldPreferThreeOverOne()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(ThreeInterfaceHandler));
        registry.Register(typeof(OneInterfaceHandler));

        var strategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();
        var descriptor = strategy.Find(typeof(MultiInterfaceMessage), registry);

        descriptor.Should().NotBeNull();
        descriptor!.MessageType.Should().Be(typeof(IThreeInterfaces));
    }

    private interface IAlphaMarker;

    private interface IBetaMarker;

    private sealed class DualMarkerMessage : IAlphaMarker, IBetaMarker;

    private sealed class AlphaMarkerHandler : IEventHandler<IAlphaMarker>
    {
        public Task HandleAsync(IAlphaMarker @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class BetaMarkerHandler : IEventHandler<IBetaMarker>
    {
        public Task HandleAsync(IBetaMarker @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private interface IFirstBase;

    private interface ISecondBase;

    private interface IThirdBase;

    private interface IThreeInterfaces : IFirstBase, ISecondBase, IThirdBase;

    private interface IOneInterface : IFirstBase;

    private sealed class MultiInterfaceMessage : IThreeInterfaces, IOneInterface;

    private sealed class ThreeInterfaceHandler : IEventHandler<IThreeInterfaces>
    {
        public Task HandleAsync(IThreeInterfaces @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class OneInterfaceHandler : IEventHandler<IOneInterface>
    {
        public Task HandleAsync(IOneInterface @event, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
