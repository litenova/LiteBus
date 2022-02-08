using System.Threading.Tasks;
using FluentAssertions;
using LiteBus.Events.Abstractions;
using LiteBus.Events.Extensions.MicrosoftDependencyInjection;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.UnitTests.Data.FakeEvent.Handlers;
using LiteBus.UnitTests.Data.FakeEvent.Messages;
using LiteBus.UnitTests.Data.FakeEvent.PostHandlers;
using LiteBus.UnitTests.Data.FakeEvent.PreHandlers;
using LiteBus.UnitTests.Data.FakeGenericEvent.Handlers;
using LiteBus.UnitTests.Data.FakeGenericEvent.Messages;
using LiteBus.UnitTests.Data.FakeGenericEvent.PostHandlers;
using LiteBus.UnitTests.Data.FakeGenericEvent.PreHandlers;
using LiteBus.UnitTests.Data.Shared.EventGlobalPostHandlers;
using LiteBus.UnitTests.Data.Shared.EventGlobalPreHandlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiteBus.UnitTests;

public class EventTests
{
    [Fact]
    public async Task SendAsync_FakeEvent_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddEvents(builder =>
                                  {
                                      // Global Handlers
                                      builder.RegisterPreHandler<FakeGlobalEventPreHandler>();
                                      builder.RegisterPostHandler<FakeGlobalEventPostHandler>();

                                      // Fake Event Handlers
                                      builder.RegisterPreHandler<FakeEventPreHandler>();
                                      builder.RegisterHandler<FakeEventHandler1>();
                                      builder.RegisterHandler<FakeEventHandler2>();
                                      builder.RegisterHandler<FakeEventHandler3>();
                                      builder.RegisterPostHandler<FakeEventPostHandler>();
                                  });
                              })
                              .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new FakeEvent();

        // Act
        await eventMediator.PublishAsync(@event);

        // Assert
        @event.ExecutedTypes.Should().HaveCount(7);
        @event.ExecutedTypes[0].Should().Be<FakeGlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<FakeEventPreHandler>();
        @event.ExecutedTypes[2].Should().Be<FakeEventHandler1>();
        @event.ExecutedTypes[3].Should().Be<FakeEventHandler2>();
        @event.ExecutedTypes[4].Should().Be<FakeEventHandler3>();
        @event.ExecutedTypes[5].Should().Be<FakeEventPostHandler>();
        @event.ExecutedTypes[6].Should().Be<FakeGlobalEventPostHandler>();
    }

    [Fact]
    public async Task SendAsync_FakeGenericEvent_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddEvents(builder =>
                                  {
                                      // Global Handlers
                                      builder.RegisterPreHandler<FakeGlobalEventPreHandler>();
                                      builder.RegisterPostHandler<FakeGlobalEventPostHandler>();

                                      // Fake Event Handlers
                                      builder.RegisterPreHandler(typeof(FakeGenericEventPreHandler<>));
                                      builder.RegisterHandler(typeof(FakeGenericEventHandler1<>));
                                      builder.RegisterHandler(typeof(FakeGenericEventHandler2<>));
                                      builder.RegisterHandler(typeof(FakeGenericEventHandler3<>));
                                      builder.RegisterPostHandler(typeof(FakeGenericEventPostHandler<>));
                                  });
                              })
                              .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var @event = new FakeGenericEvent<string>();

        // Act
        await eventMediator.PublishAsync(@event);

        // Assert
        @event.ExecutedTypes.Should().HaveCount(7);
        @event.ExecutedTypes[0].Should().Be<FakeGlobalEventPreHandler>();
        @event.ExecutedTypes[1].Should().Be<FakeGenericEventPreHandler<string>>();
        @event.ExecutedTypes[2].Should().Be<FakeGenericEventHandler1<string>>();
        @event.ExecutedTypes[3].Should().Be<FakeGenericEventHandler2<string>>();
        @event.ExecutedTypes[4].Should().Be<FakeGenericEventHandler3<string>>();
        @event.ExecutedTypes[5].Should().Be<FakeGenericEventPostHandler<string>>();
        @event.ExecutedTypes[6].Should().Be<FakeGlobalEventPostHandler>();
    }
}