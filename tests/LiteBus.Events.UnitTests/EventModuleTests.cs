using FluentAssertions;
using LiteBus.Events.Abstractions;
using LiteBus.Events.Extensions.MicrosoftDependencyInjection;
using LiteBus.Events.UnitTests.UseCases;
using LiteBus.Events.UnitTests.UseCases.ProductCreated;
using LiteBus.Events.UnitTests.UseCases.ProductUpdated;
using LiteBus.Events.UnitTests.UseCases.ProductViewed;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Events.UnitTests;

public sealed class EventModuleTests
{
    [Fact]
    public async Task Mediating_ProductCreatedEvent_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        // Act
        await eventMediator.PublishAsync(@event);

        // Assert
        @event.ExecutedTypes.Should().HaveCount(8);

        @event.ExecutedTypes[0].Should().Be<GlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<ProductCreatedEventHandlerPreHandler>();
        @event.ExecutedTypes[2].Should().Be<ProductCreatedEventHandler1>();
        @event.ExecutedTypes[3].Should().Be<ProductCreatedEventHandler2>();
        @event.ExecutedTypes[4].Should().Be<ProductCreatedEventHandler3>();
        @event.ExecutedTypes[5].Should().Be<ProductCreatedEventHandlerPostHandler1>();
        @event.ExecutedTypes[6].Should().Be<ProductCreatedEventHandlerPostHandler2>();
        @event.ExecutedTypes[7].Should().Be<GlobalEventPostHandler>();
    }
    
    [Fact]
    public async Task MediatingWithFiltered_ProductCreatedEvent_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        // Act
        await eventMediator.PublishAsync(@event, new EventMediationSettings
        {
            HandlerFilter = type => type.IsAssignableTo(typeof(IFilteredEventHandler))
        });

        // Assert
        @event.ExecutedTypes.Should().HaveCount(6);

        @event.ExecutedTypes[0].Should().Be<GlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<ProductCreatedEventHandlerPreHandler>();
        @event.ExecutedTypes[2].Should().Be<ProductCreatedEventHandler1>();
        @event.ExecutedTypes[3].Should().Be<ProductCreatedEventHandlerPostHandler1>();
        @event.ExecutedTypes[4].Should().Be<ProductCreatedEventHandlerPostHandler2>();
        @event.ExecutedTypes[5].Should().Be<GlobalEventPostHandler>();
    }
    
    [Fact]
    public async Task Mediating_ProductViewedEvent_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProductViewedEvent<>).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductViewedEvent<Mobile>
        {
            ViewSource = new Mobile()
        };

        // Act
        await eventMediator.PublishAsync(@event);

        // Assert
        @event.ExecutedTypes.Should().HaveCount(8);

        @event.ExecutedTypes[0].Should().Be<GlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<ProductViewedEventHandlerPreHandler<Mobile>>();
        @event.ExecutedTypes[2].Should().Be<ProductViewedEventHandler1<Mobile>>();
        @event.ExecutedTypes[3].Should().Be<ProductViewedEventHandler2<Mobile>>();
        @event.ExecutedTypes[4].Should().Be<ProductViewedEventHandler3<Mobile>>();
        @event.ExecutedTypes[5].Should().Be<ProductViewedEventHandlerPostHandler1<Mobile>>();
        @event.ExecutedTypes[6].Should().Be<ProductViewedEventHandlerPostHandler2<Mobile>>();
        @event.ExecutedTypes[7].Should().Be<GlobalEventPostHandler>();
    }
    
    [Fact]
    public async Task Mediating_ProductUpdatedEvent_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProductUpdatedEvent).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductUpdatedEvent();

        // Act
        await eventMediator.PublishAsync(@event);

        // Assert
        @event.ExecutedTypes.Should().HaveCount(6);

        @event.ExecutedTypes[0].Should().Be<ProductUpdatedEventHandlerPreHandler>();
        @event.ExecutedTypes[1].Should().Be<ProductUpdatedEventHandler1>();
        @event.ExecutedTypes[2].Should().Be<ProductUpdatedEventHandler2>();
        @event.ExecutedTypes[3].Should().Be<ProductUpdatedEventHandler3>();
        @event.ExecutedTypes[4].Should().Be<ProductUpdatedEventHandlerPostHandler1>();
        @event.ExecutedTypes[5].Should().Be<ProductUpdatedEventHandlerPostHandler2>();
    }
}