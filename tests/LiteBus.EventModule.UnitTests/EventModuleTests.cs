using LiteBus.EventModule.UnitTests.UseCases;
using LiteBus.EventModule.UnitTests.UseCases.EventWithNoHandlers;
using LiteBus.EventModule.UnitTests.UseCases.EventWithTag;
using LiteBus.EventModule.UnitTests.UseCases.ProblematicEvent;
using LiteBus.EventModule.UnitTests.UseCases.ProductCreated;
using LiteBus.EventModule.UnitTests.UseCases.ProductUpdated;
using LiteBus.EventModule.UnitTests.UseCases.ProductViewed;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.EventModule.UnitTests;

public sealed class EventModuleTests : LiteBusTestBase
{
    #region Basic Event Mediation Tests

    [Fact]
    public async Task mediating_simple_event_goes_through_registered_handlers_correctly()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        await eventMediator.PublishAsync(@event);

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
    public async Task mediating_generic_event_goes_through_registered_handlers_correctly()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProductViewedEvent<>).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductViewedEvent<Mobile> { ViewSource = new Mobile() };

        await eventMediator.PublishAsync(@event);

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

    #endregion

    #region Handler Priority Tests

    [Fact]
    public async Task mediating_event_with_priority_handlers_executes_in_correct_order()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProductUpdatedEvent).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductUpdatedEvent();

        await eventMediator.PublishAsync(@event);

        @event.ExecutedTypes.Should().HaveCount(6);
        @event.ExecutedTypes[0].Should().Be<ProductUpdatedEventHandlerPreHandler>();
        @event.ExecutedTypes[1].Should().Be<ProductUpdatedEventHandler1>();
        @event.ExecutedTypes[2].Should().Be<ProductUpdatedEventHandler2>();
        @event.ExecutedTypes[3].Should().Be<ProductUpdatedEventHandler3>();
        @event.ExecutedTypes[4].Should().Be<ProductUpdatedEventHandlerPostHandler1>();
        @event.ExecutedTypes[5].Should().Be<ProductUpdatedEventHandlerPostHandler2>();
    }

    #endregion

    #region Concurrency Mode Tests

    [Fact]
    public async Task mediating_event_with_sequential_priority_groups_sequential_handlers_maintains_strict_order()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        var settings = new EventMediationSettings
        {
            Execution = new EventMediationExecutionSettings
            {
                PriorityGroupsConcurrencyMode = ConcurrencyMode.Sequential,
                HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Sequential
            }
        };

        await eventMediator.PublishAsync(@event, settings);

        // Should maintain the same order as default behavior
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
    public async Task mediating_event_with_sequential_priority_groups_parallel_handlers_executes_same_priority_concurrently()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        var settings = new EventMediationSettings
        {
            Execution = new EventMediationExecutionSettings
            {
                PriorityGroupsConcurrencyMode = ConcurrencyMode.Sequential,
                HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel
            }
        };

        await eventMediator.PublishAsync(@event, settings);

        // All handlers should still execute, but order within same priority may vary
        @event.ExecutedTypes.Should().HaveCount(8);
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPreHandler));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandlerPreHandler));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandler1));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandler2));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandler3));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandlerPostHandler1));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandlerPostHandler2));
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPostHandler));
    }

    [Fact]
    public async Task mediating_event_with_parallel_priority_groups_executes_all_handlers_concurrently()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        var settings = new EventMediationSettings
        {
            Execution = new EventMediationExecutionSettings
            {
                PriorityGroupsConcurrencyMode = ConcurrencyMode.Parallel,
                HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Sequential
            }
        };

        await eventMediator.PublishAsync(@event, settings);

        // All handlers should execute, but priority order is not guaranteed
        @event.ExecutedTypes.Should().HaveCount(8);
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPreHandler));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandlerPreHandler));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandler1));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandler2));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandler3));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandlerPostHandler1));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandlerPostHandler2));
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPostHandler));
    }

    [Fact]
    public async Task mediating_event_with_all_parallel_executes_all_handlers_concurrently()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        var settings = new EventMediationSettings
        {
            Execution = new EventMediationExecutionSettings
            {
                PriorityGroupsConcurrencyMode = ConcurrencyMode.Parallel,
                HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel
            }
        };

        await eventMediator.PublishAsync(@event, settings);

        // All handlers should execute, no order guarantees
        @event.ExecutedTypes.Should().HaveCount(8);
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPreHandler));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandlerPreHandler));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandler1));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandler2));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandler3));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandlerPostHandler1));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandlerPostHandler2));
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPostHandler));
    }

    #endregion

    #region Tag-Based Routing Tests

    [Fact]
    public async Task mediating_event_with_specified_tag_goes_through_handlers_with_that_tag_and_handlers_without_any_tag_correctly()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(EventWithTag).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new EventWithTag();

        var settings = new EventMediationSettings
        {
            Routing = new EventMediationRoutingSettings
            {
                Tags = [Tags.Tag1]
            }
        };

        await eventMediator.PublishAsync(@event, settings);

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
    public async Task mediating_event_with_multiple_tags_executes_handlers_matching_any_tag()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(EventWithTag).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new EventWithTag();

        var settings = new EventMediationSettings
        {
            Routing = new EventMediationRoutingSettings
            {
                Tags = [Tags.Tag1, Tags.Tag2]
            }
        };

        await eventMediator.PublishAsync(@event, settings);

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
    public async Task mediating_event_with_handler_predicate_filter_executes_only_matching_handlers()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        var settings = new EventMediationSettings
        {
            Routing = new EventMediationRoutingSettings
            {
                HandlerPredicate = handlerDescriptor =>
                    handlerDescriptor.HandlerType.IsAssignableTo(typeof(IFilteredEventHandler)) || handlerDescriptor is not IMainHandlerDescriptor
            }
        };

        await eventMediator.PublishAsync(@event, settings);

        @event.ExecutedTypes.Should().HaveCount(6);
        @event.ExecutedTypes[0].Should().Be<GlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<ProductCreatedEventHandlerPreHandler>();
        @event.ExecutedTypes[2].Should().Be<ProductCreatedEventHandler1>();
        @event.ExecutedTypes[3].Should().Be<ProductCreatedEventHandlerPostHandler1>();
        @event.ExecutedTypes[4].Should().Be<ProductCreatedEventHandlerPostHandler2>();
        @event.ExecutedTypes[5].Should().Be<GlobalEventPostHandler>();
    }

    #endregion

    #region No Handler Found Tests

    [Fact]
    public async Task mediating_event_with_no_handlers_should_not_throw_exception_when_ThrowIfNoHandlerFound_is_false()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(EventWithNoHandlers).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new EventWithNoHandlers();

        var settings = new EventMediationSettings
        {
            ThrowIfNoHandlerFound = false
        };

        await eventMediator.PublishAsync(@event, settings);

        @event.ExecutedTypes.Should().HaveCount(0);
    }

    [Fact]
    public async Task mediating_event_with_no_handlers_should_throw_exception_when_ThrowIfNoHandlerFound_is_true()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(EventWithNoHandlers).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new EventWithNoHandlers();

        var settings = new EventMediationSettings
        {
            ThrowIfNoHandlerFound = true
        };

        var act = () => eventMediator.PublishAsync(@event, settings);

        await act.Should().ThrowAsync<NoHandlerFoundException>();
    }

    [Fact]
    public async Task mediating_event_with_filtered_out_handlers_should_throw_exception_when_ThrowIfNoHandlerFound_is_true()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        var settings = new EventMediationSettings
        {
            ThrowIfNoHandlerFound = true,
            Routing = new EventMediationRoutingSettings
            {
                HandlerPredicate = _ => false // Filter out all handlers,
            }
        };

        var act = () => eventMediator.PublishAsync(@event, settings);

        await act.Should().ThrowAsync<NoHandlerFoundException>();
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task mediating_event_with_exception_in_pre_handler_goes_through_error_handlers()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProblematicEventPreHandler).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();

        var @event = new ProblematicEvent
        {
            ThrowExceptionInType = typeof(ProblematicEventPreHandler)
        };

        await eventMediator.PublishAsync(@event);

        @event.ExecutedTypes.Should().HaveCount(5);
        @event.ExecutedTypes[0].Should().Be<GlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<ProblematicEventPreHandler>();
        @event.ExecutedTypes[2].Should().Be<GlobalEventErrorHandler>();
        @event.ExecutedTypes[3].Should().Be<ProblematicEventErrorHandler>();
        @event.ExecutedTypes[4].Should().Be<ProblematicEventErrorHandler2>();
    }

    [Fact]
    public async Task mediating_event_with_exception_in_main_handler_goes_through_error_handlers()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProblematicEventPreHandler).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();

        var @event = new ProblematicEvent
        {
            ThrowExceptionInType = typeof(ProblematicEventHandler)
        };

        await eventMediator.PublishAsync(@event);

        @event.ExecutedTypes.Should().HaveCount(6);
        @event.ExecutedTypes[0].Should().Be<GlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<ProblematicEventPreHandler>();
        @event.ExecutedTypes[2].Should().Be<ProblematicEventHandler>();
        @event.ExecutedTypes[3].Should().Be<GlobalEventErrorHandler>();
        @event.ExecutedTypes[4].Should().Be<ProblematicEventErrorHandler>();
        @event.ExecutedTypes[5].Should().Be<ProblematicEventErrorHandler2>();
    }

    [Fact]
    public async Task mediating_event_with_exception_in_post_handler_goes_through_error_handlers()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProblematicEventPreHandler).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();

        var @event = new ProblematicEvent
        {
            ThrowExceptionInType = typeof(GlobalEventPostHandler)
        };

        await eventMediator.PublishAsync(@event);

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

    #endregion

    #region Combined Settings Tests

    [Fact]
    public async Task mediating_event_with_tag_filtering_and_parallel_execution_works_correctly()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(EventWithTag).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new EventWithTag();

        var settings = new EventMediationSettings
        {
            Routing = new EventMediationRoutingSettings
            {
                Tags = [Tags.Tag1]
            },
            Execution = new EventMediationExecutionSettings
            {
                PriorityGroupsConcurrencyMode = ConcurrencyMode.Parallel,
                HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel
            }
        };

        await eventMediator.PublishAsync(@event, settings);

        // Should execute only Tag1 handlers and untagged handlers, all in parallel
        @event.ExecutedTypes.Should().HaveCount(7);
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPreHandler));
        @event.ExecutedTypes.Should().Contain(typeof(EventWithTagEventHandlerPreHandler1));
        @event.ExecutedTypes.Should().Contain(typeof(EventWithTagEventHandler1));
        @event.ExecutedTypes.Should().Contain(typeof(EventWithTagEventHandler3));
        @event.ExecutedTypes.Should().Contain(typeof(EventWithTagEventHandler4));
        @event.ExecutedTypes.Should().Contain(typeof(EventWithTagEventHandlerPostHandler1));
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPostHandler));
    }

    [Fact]
    public async Task mediating_event_with_complex_priority_structure_and_mixed_concurrency_executes_correctly()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        var settings = new EventMediationSettings
        {
            Execution = new EventMediationExecutionSettings
            {
                PriorityGroupsConcurrencyMode = ConcurrencyMode.Sequential,
                HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel
            },
            Routing = new EventMediationRoutingSettings
            {
                HandlerPredicate = handlerDescriptor =>
                {
                    var name = handlerDescriptor.HandlerType.Name;
                    return !name.Contains("Handler2");
                }

                // Exclude some handlers
            }
        };

        await eventMediator.PublishAsync(@event, settings);

        // Should execute filtered handlers with sequential priority groups but parallel within groups
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPreHandler));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandlerPreHandler));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandler1));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandler3));
        @event.ExecutedTypes.Should().Contain(typeof(ProductCreatedEventHandlerPostHandler1));
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPostHandler));

        // Should not contain Handler2 types due to predicate filter
        @event.ExecutedTypes.Should().NotContain(typeof(ProductCreatedEventHandler2));
        @event.ExecutedTypes.Should().NotContain(typeof(ProductCreatedEventHandlerPostHandler2));
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public async Task mediating_event_using_extension_method_without_settings_uses_defaults()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ProductCreatedEvent).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new ProductCreatedEvent();

        await eventMediator.PublishAsync(@event);

        // Should use default sequential behavior
        @event.ExecutedTypes.Should().HaveCount(8);
        @event.ExecutedTypes[0].Should().Be<GlobalEventPreHandler>();
        @event.ExecutedTypes[7].Should().Be<GlobalEventPostHandler>();
    }

    [Fact]
    public async Task mediating_event_using_extension_method_with_tag_works_correctly()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(EventWithTag).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new EventWithTag();

        await eventMediator.PublishAsync(@event, Tags.Tag1);

        @event.ExecutedTypes.Should().HaveCount(7);
        @event.ExecutedTypes.Should().Contain(typeof(EventWithTagEventHandler1));
        @event.ExecutedTypes.Should().NotContain(typeof(EventWithTagEventHandler2));
    }

    #endregion

    #region Performance and Edge Case Tests

    [Fact]
    public async Task mediating_event_with_no_main_handlers_but_pre_post_handlers_executes_correctly()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(EventWithNoHandlers).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new EventWithNoHandlers();

        var settings = new EventMediationSettings
        {
            ThrowIfNoHandlerFound = false
        };

        await eventMediator.PublishAsync(@event, settings);

        // Should not execute any handlers since there are no main handlers
        @event.ExecutedTypes.Should().HaveCount(0);
    }

    [Fact]
    public async Task mediating_event_with_empty_tag_collection_executes_only_untagged_handlers()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(EventWithTag).Assembly);
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new EventWithTag();

        var settings = new EventMediationSettings
        {
            Routing = new EventMediationRoutingSettings
            {
                Tags = [] // Empty collection - only untagged handlers should execute
            }
        };

        await eventMediator.PublishAsync(@event, settings);

        // Should execute only handlers without tags (GlobalEventPreHandler, GlobalEventPostHandler, and EventWithTagEventHandler3)
        @event.ExecutedTypes.Should().HaveCount(3);
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPreHandler));
        @event.ExecutedTypes.Should().Contain(typeof(EventWithTagEventHandler3)); // This one has no tags
        @event.ExecutedTypes.Should().Contain(typeof(GlobalEventPostHandler));

        // Should NOT contain tagged handlers
        @event.ExecutedTypes.Should().NotContain(typeof(EventWithTagEventHandler1)); // Has Tag1
        @event.ExecutedTypes.Should().NotContain(typeof(EventWithTagEventHandler2)); // Has Tag2
        @event.ExecutedTypes.Should().NotContain(typeof(EventWithTagEventHandler4)); // Has both tags
    }

    #endregion
}