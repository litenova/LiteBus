using LiteBus.EventModule.UnitTests.UseCases.ProductCreated;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.EventModule.UnitTests;

public sealed class EventMediatorValidationTests : LiteBusTestBase
{
    [Fact]
    public async Task PublishAsync_WithNullEvent_ThrowsArgumentNullException()
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

        var act = async () => await eventMediator.PublishAsync((IEvent) null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_WithNullGenericEvent_ThrowsArgumentNullException()
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

        var act = async () => await eventMediator.PublishAsync<ProductCreatedEvent>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
