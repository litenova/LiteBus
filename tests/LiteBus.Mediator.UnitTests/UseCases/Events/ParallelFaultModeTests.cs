using LiteBus.Events.Abstractions;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Events;

public sealed class ParallelFaultModeTests
{
    [Fact]
    public async Task PropagateFirst_waits_for_started_siblings_before_surfacing_one_failure()
    {
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddEventModule(builder =>
            {
                builder.Register<ImmediateFailureHandler>();
                builder.Register<BlockedSiblingHandler>();
            });
        }).BuildServiceProvider();

        var @event = new PropagateFirstEvent();
        var publication = serviceProvider.GetRequiredService<IEventMediator>().PublishAsync(
            @event,
            CreateParallelSettings(ParallelFaultMode.PropagateFirst));

        await @event.BothHandlersStarted.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        publication.IsCompleted.Should().BeFalse();

        @event.ReleaseSibling();

        var act = async () => await publication.ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("immediate failure");
        @event.SiblingCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task AggregateAll_waits_for_started_siblings_and_surfaces_every_failure()
    {
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddEventModule(builder =>
            {
                builder.Register<FirstAggregatedFailureHandler>();
                builder.Register<SecondAggregatedFailureHandler>();
            });
        }).BuildServiceProvider();

        var @event = new AggregateAllEvent();
        var publication = serviceProvider.GetRequiredService<IEventMediator>().PublishAsync(
            @event,
            CreateParallelSettings(ParallelFaultMode.AggregateAll));

        await @event.BothHandlersStarted.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        publication.IsCompleted.Should().BeFalse();

        @event.ReleaseHandlers();

        var act = async () => await publication.ConfigureAwait(false);
        var assertion = await act.Should().ThrowAsync<AggregateException>();

        assertion.Which.InnerExceptions.OfType<InvalidOperationException>()
            .Should().ContainSingle().Which.Message.Should().Be("first failure");
        assertion.Which.InnerExceptions.OfType<ApplicationException>()
            .Should().ContainSingle().Which.Message.Should().Be("second failure");
    }

    private static EventMediationSettings CreateParallelSettings(ParallelFaultMode faultMode)
    {
        return new EventMediationSettings
        {
            Execution = new EventExecutionSettings
            {
                HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel,
                ParallelFaultMode = faultMode
            }
        };
    }

    private sealed class PropagateFirstEvent : IEvent
    {
        private readonly TaskCompletionSource _bothHandlersStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _releaseSibling =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _startedHandlerCount;

        public Task BothHandlersStarted => _bothHandlersStarted.Task;

        public bool SiblingCompleted { get; private set; }

        public void RecordHandlerStarted()
        {
            if (Interlocked.Increment(ref _startedHandlerCount) == 2)
            {
                _bothHandlersStarted.TrySetResult();
            }
        }

        public void ReleaseSibling()
        {
            _releaseSibling.TrySetResult();
        }

        public async Task WaitForReleaseAsync(CancellationToken cancellationToken)
        {
            await _releaseSibling.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            SiblingCompleted = true;
        }
    }

    private sealed class ImmediateFailureHandler : IEventHandler<PropagateFirstEvent>
    {
        public Task HandleAsync(PropagateFirstEvent message, CancellationToken cancellationToken = default)
        {
            message.RecordHandlerStarted();
            return Task.FromException(new InvalidOperationException("immediate failure"));
        }
    }

    private sealed class BlockedSiblingHandler : IEventHandler<PropagateFirstEvent>
    {
        public async Task HandleAsync(PropagateFirstEvent message, CancellationToken cancellationToken = default)
        {
            message.RecordHandlerStarted();
            await message.WaitForReleaseAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class AggregateAllEvent : IEvent
    {
        private readonly TaskCompletionSource _bothHandlersStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _releaseHandlers =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _startedHandlerCount;

        public Task BothHandlersStarted => _bothHandlersStarted.Task;

        public void RecordHandlerStarted()
        {
            if (Interlocked.Increment(ref _startedHandlerCount) == 2)
            {
                _bothHandlersStarted.TrySetResult();
            }
        }

        public void ReleaseHandlers()
        {
            _releaseHandlers.TrySetResult();
        }

        public Task WaitForReleaseAsync(CancellationToken cancellationToken)
        {
            return _releaseHandlers.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FirstAggregatedFailureHandler : IEventHandler<AggregateAllEvent>
    {
        public async Task HandleAsync(AggregateAllEvent message, CancellationToken cancellationToken = default)
        {
            message.RecordHandlerStarted();
            await message.WaitForReleaseAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("first failure");
        }
    }

    private sealed class SecondAggregatedFailureHandler : IEventHandler<AggregateAllEvent>
    {
        public async Task HandleAsync(AggregateAllEvent message, CancellationToken cancellationToken = default)
        {
            message.RecordHandlerStarted();
            await message.WaitForReleaseAsync(cancellationToken).ConfigureAwait(false);
            throw new ApplicationException("second failure");
        }
    }
}
