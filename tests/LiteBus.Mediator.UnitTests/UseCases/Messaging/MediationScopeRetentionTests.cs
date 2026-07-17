using System.Runtime.CompilerServices;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.MediationStrategies;
using LiteBus.Messaging.Mediator;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

public sealed class MediationScopeRetentionTests : LiteBusTestBase
{
    [Fact]
    public async Task Mediate_delayed_task_retains_dispatch_scope_until_task_completes()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(DelayedScopedHandler));

        var services = new ServiceCollection()
            .AddScoped<ScopedLifetimeMarker>()
            .AddScoped<DelayedScopedHandler>();

        services.AddLiteBus(registry => registry.AddMessageModule(_ => { }));

        using var provider = services.BuildServiceProvider();

        var mediator = new MessageMediator(
            registry,
            registry,
            provider.GetRequiredService<IMessageDispatchScopeFactory>());

        var request = CreateDelayedRequest();

        var mediationTask = mediator.Mediate(new DelayedScopedCommand(), request);

        await Task.Delay(25).ConfigureAwait(false);
        DelayedScopedHandler.ActiveMarker.Should().NotBeNull();
        DelayedScopedHandler.ActiveMarker.Disposed.Should().BeFalse();

        await mediationTask.ConfigureAwait(false);

        DelayedScopedHandler.ActiveMarker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Mediate_stream_result_retains_dispatch_scope_until_enumeration_completes()
    {
        var registry = new MessageRegistry();
        registry.Register(typeof(StreamingScopedHandler));

        var services = new ServiceCollection()
            .AddScoped<ScopedLifetimeMarker>()
            .AddScoped<StreamingScopedHandler>();

        services.AddLiteBus(registry => registry.AddMessageModule(_ => { }));

        using var provider = services.BuildServiceProvider();

        var mediator = new MessageMediator(
            registry,
            registry,
            provider.GetRequiredService<IMessageDispatchScopeFactory>());

        var request = new MessageMediationRequest<StreamingScopedCommand, IAsyncEnumerable<int>>
        {
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            MessageMediationStrategy = new SingleStreamHandlerMediationStrategy<StreamingScopedCommand, int>(CancellationToken.None),
            Tags = []
        };

        var stream = mediator.Mediate(new StreamingScopedCommand(), request);

        var enumerator = stream.GetAsyncEnumerator();
        await using (enumerator.ConfigureAwait(false))
        {
            (await enumerator.MoveNextAsync().ConfigureAwait(false)).Should().BeTrue();
            StreamingScopedHandler.ActiveMarker.Should().NotBeNull();
            StreamingScopedHandler.ActiveMarker.Disposed.Should().BeFalse();

            (await enumerator.MoveNextAsync().ConfigureAwait(false)).Should().BeTrue();
            StreamingScopedHandler.ActiveMarker.Disposed.Should().BeFalse();

            (await enumerator.MoveNextAsync().ConfigureAwait(false)).Should().BeFalse();
            StreamingScopedHandler.ActiveMarker.Disposed.Should().BeTrue();
        }
    }

    private static MessageMediationRequest<DelayedScopedCommand, Task<int>> CreateDelayedRequest()
    {
        return new MessageMediationRequest<DelayedScopedCommand, Task<int>>
        {
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            MessageMediationStrategy = new ReturnHandlerTaskWithoutAwaitStrategy<DelayedScopedCommand, int>(),
            Tags = []
        };
    }

    /// <summary>
    ///     Returns the handler task without awaiting so scope retention can be observed while work is in flight.
    /// </summary>
    private sealed class ReturnHandlerTaskWithoutAwaitStrategy<TMessage, TMessageResult>
        : IMessageMediationStrategy<TMessage, Task<TMessageResult>>
        where TMessage : notnull
    {
        public Task<TMessageResult> Mediate(
            TMessage message,
            IMessageDependencies messageDependencies,
            IExecutionContext executionContext)
        {
            var handler = SingleMainHandlerResolver.Resolve<TMessage>(messageDependencies).Handler.Value;
            return HandlerInvocation.InvokeMainHandlerAsync<TMessage, TMessageResult>(
                handler,
                message,
                executionContext.CancellationToken);
        }
    }

    private sealed record DelayedScopedCommand : ICommand<int>;

    private sealed record StreamingScopedCommand : ICommand;

    private sealed class ScopedLifetimeMarker : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DelayedScopedHandler : ICommandHandler<DelayedScopedCommand, int>
    {
        internal static ScopedLifetimeMarker? ActiveMarker { get; private set; }

        private readonly ScopedLifetimeMarker _marker;

        public DelayedScopedHandler(ScopedLifetimeMarker marker)
        {
            _marker = marker;
        }

        public async Task<int> HandleAsync(DelayedScopedCommand command, CancellationToken cancellationToken = default)
        {
            ActiveMarker = _marker;
            await Task.Delay(75, cancellationToken).ConfigureAwait(false);
            return 1;
        }
    }

    private sealed class StreamingScopedHandler : IStreamMessageHandler<StreamingScopedCommand, int>
    {
        internal static ScopedLifetimeMarker? ActiveMarker { get; private set; }

        private readonly ScopedLifetimeMarker _marker;

        public StreamingScopedHandler(ScopedLifetimeMarker marker)
        {
            _marker = marker;
        }

        public async IAsyncEnumerable<int> StreamAsync(
            StreamingScopedCommand command,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ActiveMarker = _marker;
            yield return 1;
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            yield return 2;
        }
    }
}
