using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.MessageModule.UnitTests;

/// <summary>
/// Contains tests for the event handler predicate filtering feature, ensuring that
/// only handlers matching the predicate are executed.
/// </summary>
[Collection("Sequential")]
public sealed class PredicateFilteringTests : LiteBusTestBase
{
    // Test Components
    private interface IFilterableHandler;

    private sealed record FilteredEvent : IEvent
    {
        public List<Type> ExecutedTypes { get; } = [];
    }

    private sealed class FilterableEventHandler : IEventHandler<FilteredEvent>, IFilterableHandler
    {
        public Task HandleAsync(FilteredEvent message, CancellationToken cancellationToken = default)
        {
            message.ExecutedTypes.Add(GetType());
            return Task.CompletedTask;
        }
    }

    private sealed class AnotherFilterableEventHandler : IEventHandler<FilteredEvent>, IFilterableHandler
    {
        public Task HandleAsync(FilteredEvent message, CancellationToken cancellationToken = default)
        {
            message.ExecutedTypes.Add(GetType());
            return Task.CompletedTask;
        }
    }

    private sealed class NonFilterableEventHandler : IEventHandler<FilteredEvent>
    {
        public Task HandleAsync(FilteredEvent message, CancellationToken cancellationToken = default)
        {
            message.ExecutedTypes.Add(GetType());
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publish_Event_WithPredicate_ShouldExecuteOnlyMatchingHandlers()
    {
        // ARRANGE
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddEventModule(builder =>
            {
                builder.Register<FilterableEventHandler>();
                builder.Register<AnotherFilterableEventHandler>();
                builder.Register<NonFilterableEventHandler>();
            });
        }).BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new FilteredEvent();

        var settings = new EventMediationSettings
        {
            Routing = new EventMediationRoutingSettings
            {
                // The predicate filters for handlers that implement our marker interface.
                HandlerPredicate = descriptor => descriptor.HandlerType.IsAssignableTo(typeof(IFilterableHandler))
            }
        };

        // ACT
        await eventMediator.PublishAsync(@event, settings);

        // ASSERT
        @event.ExecutedTypes.Should().HaveCount(2);
        @event.ExecutedTypes.Should().Contain(typeof(FilterableEventHandler));
        @event.ExecutedTypes.Should().Contain(typeof(AnotherFilterableEventHandler));
        @event.ExecutedTypes.Should().NotContain(typeof(NonFilterableEventHandler));
    }
}