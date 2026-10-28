using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Events;

/// <summary>
///     Verifies parallel ContinueAndAggregate rethrows cancellation instead of aggregating it, while mixed cancellation
///     and faults still aggregate.
/// </summary>
public sealed class ParallelCancellationTests
{
    [Fact]
    public async Task ContinueAndAggregate_with_all_handlers_canceled_should_rethrow_cancellation_without_error_handlers()
    {
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder =>
            {
                builder.Register<FirstCancelingHandler>();
                builder.Register<SecondCancelingHandler>();
                builder.Register<RecordingAllCanceledErrorHandler>();
            });
        }).BuildServiceProvider();

        var @event = new AllCanceledEvent();

        var act = () => serviceProvider.GetRequiredService<IEventMediator>().PublishAsync(
            @event,
            CreateParallelAggregateSettings());

        // Cancellation is not a handler fault: the publish reports Canceled, error handlers never run, and callers
        // catching OperationCanceledException see it.
        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        @event.ErrorHandlerRan.Should().BeFalse();
    }

    [Fact]
    public async Task ContinueAndAggregate_with_mixed_cancellation_and_fault_should_aggregate()
    {
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder =>
            {
                builder.Register<CancelingMixedHandler>();
                builder.Register<FailingMixedHandler>();
                builder.Register<RecordingMixedErrorHandler>();
            });
        }).BuildServiceProvider();

        var @event = new MixedFaultEvent();

        var act = () => serviceProvider.GetRequiredService<IEventMediator>().PublishAsync(
            @event,
            CreateParallelAggregateSettings());

        var assertion = await act.Should().ThrowAsync<AggregateException>().ConfigureAwait(false);

        assertion.Which.InnerExceptions.Should().HaveCount(2);
        assertion.Which.InnerExceptions.OfType<OperationCanceledException>().Should().ContainSingle();
        assertion.Which.InnerExceptions.OfType<InvalidOperationException>().Should().ContainSingle();
        @event.ErrorHandlerRan.Should().BeTrue();
    }

    private static EventMediationSettings CreateParallelAggregateSettings()
    {
        return new EventMediationSettings
        {
            Execution = new EventExecutionSettings
            {
                HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel,
                FaultMode = HandlerFaultMode.ContinueAndAggregate
            }
        };
    }

    private sealed class AllCanceledEvent : IEvent
    {
        public bool ErrorHandlerRan { get; set; }
    }

    private sealed class FirstCancelingHandler : IEventHandler<AllCanceledEvent>
    {
        public Task HandleAsync(AllCanceledEvent message, CancellationToken cancellationToken = default)
        {
            throw new OperationCanceledException();
        }
    }

    private sealed class SecondCancelingHandler : IEventHandler<AllCanceledEvent>
    {
        public Task HandleAsync(AllCanceledEvent message, CancellationToken cancellationToken = default)
        {
            throw new OperationCanceledException();
        }
    }

    private sealed class RecordingAllCanceledErrorHandler : IEventErrorHandler<AllCanceledEvent>
    {
        public Task HandleErrorAsync(
            MessageErrorContext<AllCanceledEvent, object> context,
            CancellationToken cancellationToken = default)
        {
            context.Message.ErrorHandlerRan = true;
            return Task.CompletedTask;
        }
    }

    private sealed class MixedFaultEvent : IEvent
    {
        public bool ErrorHandlerRan { get; set; }
    }

    private sealed class CancelingMixedHandler : IEventHandler<MixedFaultEvent>
    {
        public Task HandleAsync(MixedFaultEvent message, CancellationToken cancellationToken = default)
        {
            throw new OperationCanceledException();
        }
    }

    private sealed class FailingMixedHandler : IEventHandler<MixedFaultEvent>
    {
        public Task HandleAsync(MixedFaultEvent message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("mixed failure");
        }
    }

    private sealed class RecordingMixedErrorHandler : IEventErrorHandler<MixedFaultEvent>
    {
        public Task HandleErrorAsync(
            MessageErrorContext<MixedFaultEvent, object> context,
            CancellationToken cancellationToken = default)
        {
            context.Message.ErrorHandlerRan = true;
            return Task.CompletedTask;
        }
    }
}
