using LiteBus.Mediator.UnitTests.UseCases.Events.UseCases.ProductCreated;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Events;

public sealed class EventMediatorValidationTests : LiteBusTestBase
{
    [Fact]
    public async Task PublishAsync_WithNullEvent_ThrowsArgumentNullException()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.RegisterFromEventsTestAssembly();
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();

        var act = async () => await eventMediator.PublishAsync(null!).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_WithNullGenericEvent_ThrowsArgumentNullException()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.RegisterFromEventsTestAssembly();
                });
            })
            .BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();

        var act = async () => await eventMediator.PublishAsync<ProductCreatedEvent>(null!).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
