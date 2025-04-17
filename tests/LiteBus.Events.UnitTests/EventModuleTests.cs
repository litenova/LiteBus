using FluentAssertions;
using LiteBus.Events.Abstractions;
using LiteBus.Events.Extensions.MicrosoftDependencyInjection;
using LiteBus.Events.UnitTests.UseCases;
using LiteBus.Events.UnitTests.UseCases.EventWithNoHandlers;
using LiteBus.Events.UnitTests.UseCases.EventWithTag;
using LiteBus.Events.UnitTests.UseCases.ProblematicEvent;
using LiteBus.Events.UnitTests.UseCases.ProductCreated;
using LiteBus.Events.UnitTests.UseCases.ProductUpdated;
using LiteBus.Events.UnitTests.UseCases.ProductViewed;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Events.UnitTests;

public sealed class EventModuleTests
{
    [Fact]
    public async Task mediating_simple_event_goes_through_registered_handlers_correctly()
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
    public async Task mediating_ProductCreatedEvent_with_filtered_handlers_goes_through_registered_handlers_correctly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        // Act
        await eventMediator.PublishAsync(@event,
            new EventMediationSettings
            {
                Filters =
                {
                    HandlerPredicate = type => type.IsAssignableTo(typeof(IFilteredEventHandler))
                }
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
    public async Task mediating_ProductCreatedEvent_with_no_handler_throw_exception_when_ThrowIfNoHandlerFound_is_set_true()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        // Act
        var act = () => eventMediator.PublishAsync(@event,
            new EventMediationSettings
            {
                ThrowIfNoHandlerFound = true,
                Filters =
                {
                    HandlerPredicate = _ => false
                }
            });

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task mediating_generic_event_goes_through_registered_handlers_correctly()
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

    [Fact]
    public async Task mediating_a_event_with_exception_in_pre_handler_goes_through_error_handlers()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProblematicEventPreHandler).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();

        var @event = new ProblematicEvent
        {
            ThrowExceptionInType = typeof(ProblematicEventPreHandler)
        };

        // Act
        await eventMediator.PublishAsync(@event);

        // Assert
        @event.ExecutedTypes.Should().HaveCount(5);

        @event.ExecutedTypes[0].Should().Be<GlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<ProblematicEventPreHandler>();
        @event.ExecutedTypes[2].Should().Be<GlobalEventErrorHandler>();
        @event.ExecutedTypes[3].Should().Be<ProblematicEventErrorHandler>();
        @event.ExecutedTypes[4].Should().Be<ProblematicEventErrorHandler2>();
    }

    [Fact]
    public async Task mediating_a_event_with_exception_in_post_global_handler_goes_through_error_handlers()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProblematicEventPreHandler).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();

        var @event = new ProblematicEvent
        {
            ThrowExceptionInType = typeof(GlobalEventPostHandler)
        };

        // Act
        await eventMediator.PublishAsync(@event);

        // Assert
        @event.ExecutedTypes.Should().HaveCount(8);
        @event.ExecutedTypes[0].Should().Be<GlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<ProblematicEventPreHandler>();
        @event.ExecutedTypes[2].Should().Be<ProblematicEventHandler>();
        @event.ExecutedTypes[3].Should().Be<ProblematicEventPostHandler>();
        @event.ExecutedTypes[4].Should().Be<GlobalEventPostHandler>();
        @event.ExecutedTypes[5].Should().Be<GlobalEventErrorHandler>();
        @event.ExecutedTypes[6].Should().Be<ProblematicEventErrorHandler>();
        @event.ExecutedTypes[7].Should().Be<ProblematicEventErrorHandler2>();
    }

    [Fact]
    public async Task mediating_an_event_with_specified_tag_goes_through_handlers_with_that_tag_and_handlers_without_any_tag_correctly()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProblematicEventPreHandler).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();

        var @event = new EventWithTag();

        var settings = new EventMediationSettings
        {
            Filters =
            {
                Tags = [Tags.Tag1]
            }
        };

        // Act
        await eventMediator.PublishAsync(@event, settings);

        // Assert
        @event.ExecutedTypes.Should().HaveCount(7);
        @event.ExecutedTypes[0].Should().Be<GlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<EventWithTagEventHandlerPreHandler1>();
        @event.ExecutedTypes[2].Should().Be<EventWithTagEventHandler1>();
        @event.ExecutedTypes[3].Should().Be<EventWithTagEventHandler3>();
        @event.ExecutedTypes[4].Should().Be<EventWithTagEventHandler4>();
        @event.ExecutedTypes[5].Should().Be<EventWithTagEventHandlerPostHandler1>();
        @event.ExecutedTypes[6].Should().Be<GlobalEventPostHandler>();
    }

    [Fact]
    public async Task mediating_the_an_event_with_both_all_available_tags_goes_through_all_handlers_correctly()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProblematicEventPreHandler).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();

        var @event = new EventWithTag();

        var settings = new EventMediationSettings
        {
            Filters =
            {
                Tags = [Tags.Tag1, Tags.Tag2]
            }
        };

        // Act
        await eventMediator.PublishAsync(@event, settings);

        // Assert
        @event.ExecutedTypes.Should().HaveCount(10);
        @event.ExecutedTypes[0].Should().Be<GlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<EventWithTagEventHandlerPreHandler1>();
        @event.ExecutedTypes[2].Should().Be<EventWithTagEventHandlerPreHandler2>();
        @event.ExecutedTypes[3].Should().Be<EventWithTagEventHandler1>();
        @event.ExecutedTypes[4].Should().Be<EventWithTagEventHandler2>();
        @event.ExecutedTypes[5].Should().Be<EventWithTagEventHandler3>();
        @event.ExecutedTypes[6].Should().Be<EventWithTagEventHandler4>();
        @event.ExecutedTypes[7].Should().Be<EventWithTagEventHandlerPostHandler1>();
        @event.ExecutedTypes[8].Should().Be<EventWithTagEventHandlerPostHandler2>();
        @event.ExecutedTypes[9].Should().Be<GlobalEventPostHandler>();
    }

    [Fact]
    public async Task mediating_the_an_event_with_no_handlers_should_not_throw_exception_when_ThrowIfNoHandlerFound_is_set_false()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProblematicEventPreHandler).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();

        var @event = new EventWithNoHandlers();

        var settings = new EventMediationSettings
        {
            ThrowIfNoHandlerFound = false
        };

        // Act
        await eventMediator.PublishAsync(@event, settings);

        // Assert
        @event.ExecutedTypes.Should().HaveCount(0);
    }

    [Fact]
    public async Task mediating_the_an_event_with_no_handlers_should_throw_exception_when_ThrowIfNoHandlerFound_is_set_true()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddEventModule(builder => { builder.RegisterFromAssembly(typeof(ProblematicEventPreHandler).Assembly); }); })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();

        var @event = new EventWithNoHandlers();

        var settings = new EventMediationSettings
        {
            ThrowIfNoHandlerFound = true
        };

        // Act
        var act = () => eventMediator.PublishAsync(@event, settings);

        // Assert
        await act.Should().ThrowAsync<NoHandlerFoundException>();
    }
}