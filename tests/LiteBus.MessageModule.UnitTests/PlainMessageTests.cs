using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.MessageModule.UnitTests.Data.PlainMessage;
using LiteBus.MessageModule.UnitTests.Data.Shared.CommandGlobalPostHandlers;
using LiteBus.Messaging;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.MessageModule.UnitTests;

[Collection("Sequential")]
public sealed class PlainMessageTests : LiteBusTestBase
{
    [Fact]
    public async Task Send_FakeCommand_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddMessageModule(builder =>
                {
                    // Global Handlers
                    builder.Register<FakeGlobalCommandPostHandler>();

                    // Fake Command Handlers
                    builder.Register<FakePlainMessageAsyncHandler1>();
                    builder.Register<FakePlainMessageAsyncHandler2>();
                    builder.Register<FakePlainMessageAsyncHandler3>();
                });

                configuration.AddEventModule();
            })
            .BuildServiceProvider();

        var eventPublisher = serviceProvider.GetRequiredService<IEventPublisher>();
        var plainMessage = new FakePlainMessage();

        // Act
        await eventPublisher.PublishAsync(plainMessage);

        // Assert
        plainMessage.ExecutedTypes.Should().HaveCount(3);
        plainMessage.ExecutedTypes[0].Should().Be<FakePlainMessageAsyncHandler1>();
        plainMessage.ExecutedTypes[1].Should().Be<FakePlainMessageAsyncHandler2>();
        plainMessage.ExecutedTypes[2].Should().Be<FakePlainMessageAsyncHandler3>();
    }
}