using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.EventModule.UnitTests;

public sealed class ParallelExecutionContextTests : LiteBusTestBase
{
    private sealed record ParallelContextEvent : IEvent
    {
        public List<string> ObservedItems { get; } = [];
    }

    private sealed class ParallelContextEventHandler1 : IEventHandler<ParallelContextEvent>
    {
        public Task HandleAsync(ParallelContextEvent message, CancellationToken cancellationToken = default)
        {
            if (AmbientExecutionContext.Current.Items.TryGetValue("Marker", out var value))
            {
                lock (message.ObservedItems)
                {
                    message.ObservedItems.Add((string) value);
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ParallelContextEventHandler2 : IEventHandler<ParallelContextEvent>
    {
        public Task HandleAsync(ParallelContextEvent message, CancellationToken cancellationToken = default)
        {
            if (AmbientExecutionContext.Current.Items.TryGetValue("Marker", out var value))
            {
                lock (message.ObservedItems)
                {
                    message.ObservedItems.Add((string) value);
                }
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_WithParallelHandlers_PropagatesExecutionContextToEachHandler()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.Register<ParallelContextEventHandler1>();
                    builder.Register<ParallelContextEventHandler2>();
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ParallelContextEvent();

        var settings = new EventMediationSettings
        {
            Items = { ["Marker"] = "ContextValue" },
            Execution = new EventMediationExecutionSettings
            {
                PriorityGroupsConcurrencyMode = ConcurrencyMode.Parallel,
                HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel
            }
        };

        await eventMediator.PublishAsync(@event, settings);

        @event.ObservedItems.Should().HaveCount(2);
        @event.ObservedItems.Should().OnlyContain(item => item == "ContextValue");
    }
}
