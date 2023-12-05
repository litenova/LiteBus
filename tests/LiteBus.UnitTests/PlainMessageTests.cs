using System.Threading.Tasks;
using FluentAssertions;
using LiteBus.Events.Abstractions;
using LiteBus.Events.Extensions.MicrosoftDependencyInjection;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.UnitTests.Data.PlainMessage;
using LiteBus.UnitTests.Data.Shared.CommandGlobalPostHandlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiteBus.UnitTests;

public sealed class PlainMessageTests
{
    [Fact]
    public async Task Send_FakeCommand_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(_ => { }).AddMessageModule(builder =>
                {
                    // Global Handlers
                    builder.Register<FakeGlobalCommandPostHandler>();

                    // Fake Command Handlers
                    builder.Register<FakePlainMessageAsyncHandler1>();
                    builder.Register<FakePlainMessageAsyncHandler2>();
                    builder.Register<FakePlainMessageAsyncHandler3>();
                });
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