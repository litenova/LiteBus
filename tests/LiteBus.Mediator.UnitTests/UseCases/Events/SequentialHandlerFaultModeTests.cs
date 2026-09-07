using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Events;

/// <summary>
///     Covers fault isolation for sequentially executed event handlers, where a failing subscriber used to suppress
///     every subscriber registered behind it.
/// </summary>
public sealed class SequentialHandlerFaultModeTests
{
    [Fact]
    public async Task PropagateFirst_stops_the_group_at_the_first_failure()
    {
        var log = new HandlerLog();
        await using var serviceProvider = BuildProvider(log);

        var act = async () => await serviceProvider.GetRequiredService<IEventMediator>()
            .PublishAsync(new SequentialEvent(), CreateSettings(HandlerFaultMode.PropagateFirst))
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("second handler failed");

        log.Executed.Should().Equal("first", "second");
    }

    [Fact]
    public async Task ContinueAndAggregate_runs_the_handlers_behind_a_failure()
    {
        var log = new HandlerLog();
        await using var serviceProvider = BuildProvider(log);

        var act = async () => await serviceProvider.GetRequiredService<IEventMediator>()
            .PublishAsync(new SequentialEvent(), CreateSettings(HandlerFaultMode.ContinueAndAggregate))
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("second handler failed");

        log.Executed.Should().Equal("first", "second", "third");
    }

    [Fact]
    public async Task ContinueAndAggregate_surfaces_every_failure_in_the_group()
    {
        var log = new HandlerLog();
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder =>
            {
                builder.Register<FirstFailingHandler>();
                builder.Register<SecondFailingHandler>();
            });
        }).AddSingleton(log).BuildServiceProvider();

        var act = async () => await serviceProvider.GetRequiredService<IEventMediator>()
            .PublishAsync(new TwoFailureEvent(), CreateSettings(HandlerFaultMode.ContinueAndAggregate))
            .ConfigureAwait(false);

        var assertion = await act.Should().ThrowAsync<AggregateException>();

        assertion.Which.InnerExceptions.OfType<InvalidOperationException>()
            .Should().ContainSingle().Which.Message.Should().Be("first failure");
        assertion.Which.InnerExceptions.OfType<ApplicationException>()
            .Should().ContainSingle().Which.Message.Should().Be("second failure");
        log.Executed.Should().Equal("first", "second");
    }

    [Fact]
    public async Task ContinueAndAggregate_does_not_run_a_later_priority_group_after_a_failure()
    {
        var log = new HandlerLog();
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder =>
            {
                builder.Register<EarlyGroupFailingHandler>();
                builder.Register<LateGroupHandler>();
            });
        }).AddSingleton(log).BuildServiceProvider();

        var act = async () => await serviceProvider.GetRequiredService<IEventMediator>()
            .PublishAsync(new PriorityGroupEvent(), CreateSettings(HandlerFaultMode.ContinueAndAggregate))
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("early group failed");

        log.Executed.Should().Equal("early");
    }

    private static ServiceProvider BuildProvider(HandlerLog log)
    {
        return new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder =>
            {
                builder.Register<FirstSequentialHandler>();
                builder.Register<SecondSequentialHandler>();
                builder.Register<ThirdSequentialHandler>();
            });
        }).AddSingleton(log).BuildServiceProvider();
    }

    private static EventMediationSettings CreateSettings(HandlerFaultMode faultMode)
    {
        return new EventMediationSettings
        {
            Execution = new EventExecutionSettings
            {
                HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Sequential,
                PriorityGroupsConcurrencyMode = ConcurrencyMode.Sequential,
                FaultMode = faultMode
            }
        };
    }

    /// <summary>
    ///     Records the order in which handlers ran so a suppressed sibling is visible as an absent entry.
    /// </summary>
    public sealed class HandlerLog
    {
        /// <summary>
        ///     The handler names in execution order.
        /// </summary>
        private readonly List<string> _executed = [];

        /// <summary>
        ///     Gets the names of the handlers that ran, in order.
        /// </summary>
        public IReadOnlyList<string> Executed => _executed;

        /// <summary>
        ///     Records that a handler ran.
        /// </summary>
        /// <param name="name">The handler name.</param>
        public void Record(string name)
        {
            _executed.Add(name);
        }
    }

    private sealed class SequentialEvent : IEvent;

    private sealed class TwoFailureEvent : IEvent;

    private sealed class PriorityGroupEvent : IEvent;

    private sealed class FirstSequentialHandler : IEventHandler<SequentialEvent>
    {
        private readonly HandlerLog _log;

        public FirstSequentialHandler(HandlerLog log)
        {
            _log = log;
        }

        public Task HandleAsync(SequentialEvent message, CancellationToken cancellationToken = default)
        {
            _log.Record("first");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondSequentialHandler : IEventHandler<SequentialEvent>
    {
        private readonly HandlerLog _log;

        public SecondSequentialHandler(HandlerLog log)
        {
            _log = log;
        }

        public Task HandleAsync(SequentialEvent message, CancellationToken cancellationToken = default)
        {
            _log.Record("second");
            throw new InvalidOperationException("second handler failed");
        }
    }

    private sealed class ThirdSequentialHandler : IEventHandler<SequentialEvent>
    {
        private readonly HandlerLog _log;

        public ThirdSequentialHandler(HandlerLog log)
        {
            _log = log;
        }

        public Task HandleAsync(SequentialEvent message, CancellationToken cancellationToken = default)
        {
            _log.Record("third");
            return Task.CompletedTask;
        }
    }

    private sealed class FirstFailingHandler : IEventHandler<TwoFailureEvent>
    {
        private readonly HandlerLog _log;

        public FirstFailingHandler(HandlerLog log)
        {
            _log = log;
        }

        public Task HandleAsync(TwoFailureEvent message, CancellationToken cancellationToken = default)
        {
            _log.Record("first");
            throw new InvalidOperationException("first failure");
        }
    }

    private sealed class SecondFailingHandler : IEventHandler<TwoFailureEvent>
    {
        private readonly HandlerLog _log;

        public SecondFailingHandler(HandlerLog log)
        {
            _log = log;
        }

        public Task HandleAsync(TwoFailureEvent message, CancellationToken cancellationToken = default)
        {
            _log.Record("second");
            throw new ApplicationException("second failure");
        }
    }

    [HandlerPriority(1)]
    private sealed class EarlyGroupFailingHandler : IEventHandler<PriorityGroupEvent>
    {
        private readonly HandlerLog _log;

        public EarlyGroupFailingHandler(HandlerLog log)
        {
            _log = log;
        }

        public Task HandleAsync(PriorityGroupEvent message, CancellationToken cancellationToken = default)
        {
            _log.Record("early");
            throw new InvalidOperationException("early group failed");
        }
    }

    [HandlerPriority(2)]
    private sealed class LateGroupHandler : IEventHandler<PriorityGroupEvent>
    {
        private readonly HandlerLog _log;

        public LateGroupHandler(HandlerLog log)
        {
            _log = log;
        }

        public Task HandleAsync(PriorityGroupEvent message, CancellationToken cancellationToken = default)
        {
            _log.Record("late");
            return Task.CompletedTask;
        }
    }
}
