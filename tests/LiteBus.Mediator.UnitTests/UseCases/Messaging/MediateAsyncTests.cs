using LiteBus.Commands.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.MediationStrategies;
using LiteBus.Messaging.Mediator;
using LiteBus.Messaging.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

public sealed class MediateAsyncTests
{
    [Fact]
    public async Task MediateAsync_WhenStrategyReturnsTask_ShouldReturnSameTaskInstance()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(MediateAsyncProbeHandler));
        var serviceProvider = new ServiceCollection()
            .AddTransient<MediateAsyncProbeHandler>()
            .BuildServiceProvider();
        var mediator = new MessageMediator(registry, registry, new RootMessageDispatchScopeFactory(serviceProvider));

        var request = new MessageMediationRequest<MediateAsyncProbeCommand, Task>
        {
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            MessageMediationStrategy = new SingleAsyncHandlerMediationStrategy<MediateAsyncProbeCommand>(),
            Tags = []
        };

        var result = await mediator.MediateAsync(new MediateAsyncProbeCommand(), request).ConfigureAwait(false);

        result.Should().BeSameAs(MediateAsyncProbeHandler.CompletedTask);
    }

    private sealed record MediateAsyncProbeCommand : ICommand;

    private sealed class MediateAsyncProbeHandler : ICommandHandler<MediateAsyncProbeCommand>
    {
        internal static readonly Task CompletedTask = Task.CompletedTask;

        public Task HandleAsync(MediateAsyncProbeCommand command, CancellationToken cancellationToken = default)
            => CompletedTask;
    }
}
