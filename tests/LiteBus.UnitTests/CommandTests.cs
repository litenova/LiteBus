using System.Threading.Tasks;
using FluentAssertions;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Extensions.MicrosoftDependencyInjection;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.UnitTests.Data.FakeCommand.Handlers;
using LiteBus.UnitTests.Data.FakeCommand.Messages;
using LiteBus.UnitTests.Data.FakeCommand.PostHandlers;
using LiteBus.UnitTests.Data.FakeCommand.PreHandlers;
using LiteBus.UnitTests.Data.FakeGenericCommand.Handlers;
using LiteBus.UnitTests.Data.FakeGenericCommand.Messages;
using LiteBus.UnitTests.Data.FakeGenericCommand.PostHandlers;
using LiteBus.UnitTests.Data.FakeGenericCommand.PreHandlers;
using LiteBus.UnitTests.Data.Shared.CommandGlobalPostHandlers;
using LiteBus.UnitTests.Data.Shared.CommandGlobalPreHandlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiteBus.UnitTests;

public class CommandTests
{
    [Fact]
    public async Task SendAsync_FakeCommand_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddCommands(builder =>
                                  {
                                      // Global Handlers
                                      builder.RegisterPreHandler<FakeGlobalCommandPreHandler>();
                                      builder.RegisterPostHandler<FakeGlobalCommandPostHandler>();

                                      // Fake Command Handlers
                                      builder.RegisterPreHandler<FakeCommandPreHandler>();
                                      builder.RegisterHandler<FakeCommandHandler>();
                                      builder.RegisterPostHandler<FakeCommandPostHandler>();
                                  });
                              })
                              .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new FakeCommand();

        // Act
        var commandResult = await commandMediator.SendAsync(command);

        // Assert
        commandResult.CorrelationId.Should().Be(command.CorrelationId);
        command.ExecutedTypes.Should().HaveCount(5);
        command.ExecutedTypes[0].Should().Be<FakeGlobalCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<FakeCommandPreHandler>();
        command.ExecutedTypes[2].Should().Be<FakeCommandHandler>();
        command.ExecutedTypes[3].Should().Be<FakeCommandPostHandler>();
        command.ExecutedTypes[4].Should().Be<FakeGlobalCommandPostHandler>();
    }

    [Fact]
    public async Task SendAsync_FakeGenericCommand_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddCommands(builder =>
                                  {
                                      // Global Handlers
                                      builder.RegisterPreHandler<FakeGlobalCommandPreHandler>();
                                      builder.RegisterPostHandler<FakeGlobalCommandPostHandler>();

                                      // Fake Command Handlers
                                      builder.RegisterPreHandler(typeof(FakeGenericCommandPreHandler<>));
                                      builder.RegisterHandler(typeof(FakeGenericCommandHandlerWithoutResult<>));
                                      builder.RegisterPostHandler(typeof(FakeGenericCommandPostHandler<>));
                                  });
                              })
                              .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new FakeGenericCommand<string>();

        // Act
        var commandResult = await commandMediator.SendAsync(command);

        // Assert
        commandResult.CorrelationId.Should().Be(command.CorrelationId);
        command.ExecutedTypes.Should().HaveCount(5);
        command.ExecutedTypes[0].Should().Be<FakeGlobalCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<FakeGenericCommandPreHandler<string>>();
        command.ExecutedTypes[2].Should().Be<FakeGenericCommandHandlerWithoutResult<string>>();
        command.ExecutedTypes[3].Should().Be<FakeGenericCommandPostHandler<string>>();
        command.ExecutedTypes[4].Should().Be<FakeGlobalCommandPostHandler>();
    }
    
    [Fact]
    public void Send_FakeCommand_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddCommands(builder =>
                                  {
                                      // Global Handlers
                                      builder.RegisterPreHandler<FakeGlobalCommandPreHandler>();
                                      builder.RegisterPostHandler<FakeGlobalCommandPostHandler>();

                                      // Fake Command Handlers
                                      builder.RegisterPreHandler<FakeCommandPreHandler>();
                                      builder.RegisterHandler<FakeCommandHandler>();
                                      builder.RegisterHandler<FakeSyncCommandHandler>();
                                      builder.RegisterPostHandler<FakeCommandPostHandler>();
                                  });
                              })
                              .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new FakeCommand();

        // Act
        var commandResult = commandMediator.Send(command);

        // Assert
        commandResult.CorrelationId.Should().Be(command.CorrelationId);
        command.ExecutedTypes.Should().HaveCount(5);
        command.ExecutedTypes[0].Should().Be<FakeGlobalCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<FakeCommandPreHandler>();
        command.ExecutedTypes[2].Should().Be<FakeSyncCommandHandler>();
        command.ExecutedTypes[3].Should().Be<FakeCommandPostHandler>();
        command.ExecutedTypes[4].Should().Be<FakeGlobalCommandPostHandler>();
    }
}