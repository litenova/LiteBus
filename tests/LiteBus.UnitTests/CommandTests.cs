using System.Threading.Tasks;
using FluentAssertions;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Extensions.MicrosoftDependencyInjection;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.UnitTests.Data.FakeCommand.Handlers;
using LiteBus.UnitTests.Data.FakeCommand.Messages;
using LiteBus.UnitTests.Data.FakeCommand.PostHandlers;
using LiteBus.UnitTests.Data.FakeCommand.PreHandlers;
using LiteBus.UnitTests.Data.FakeCommandWithoutResult.Handlers;
using LiteBus.UnitTests.Data.FakeCommandWithoutResult.Messages;
using LiteBus.UnitTests.Data.FakeCommandWithoutResult.PostHandlers;
using LiteBus.UnitTests.Data.FakeCommandWithoutResult.PreHandlers;
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
                                      builder.Register<FakeGlobalCommandPreHandler>();
                                      builder.Register<FakeGlobalCommandPostHandler>();

                                      // Fake Command Handlers
                                      builder.Register<FakeCommandPreHandler>();
                                      builder.Register<FakeCommandHandler>();
                                      builder.Register<FakeCommandPostHandler>();
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
    public async Task SendAsync_FakeCommandWithoutResult_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddCommands(builder =>
                                  {
                                      // Global Handlers
                                      builder.Register<FakeGlobalCommandPreHandler>();
                                      builder.Register<FakeGlobalCommandPostHandler>();

                                      // Fake Command Handlers
                                      builder.Register<FakeCommandWithoutResultPreHandler>();
                                      builder.Register<FakeCommandWithoutResultCommandHandler>();
                                      builder.Register<FakeCommandWithoutResultPostHandler>();
                                  });
                              })
                              .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new FakeCommandWithoutResult();

        // Act
        await commandMediator.SendAsync(command);

        // Assert
        command.ExecutedTypes.Should().HaveCount(5);
        command.ExecutedTypes[0].Should().Be<FakeGlobalCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<FakeCommandWithoutResultPreHandler>();
        command.ExecutedTypes[2].Should().Be<FakeCommandWithoutResultCommandHandler>();
        command.ExecutedTypes[3].Should().Be<FakeCommandWithoutResultPostHandler>();
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
                                      builder.Register<FakeGlobalCommandPreHandler>();
                                      builder.Register<FakeGlobalCommandPostHandler>();

                                      // Fake Command Handlers
                                      builder.Register(typeof(FakeGenericCommandPreHandler<>));
                                      builder.Register(typeof(FakeGenericCommandHandlerWithoutResult<>));
                                      builder.Register(typeof(FakeGenericCommandPostHandler<>));
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
                                      builder.Register<FakeGlobalSyncCommandPreHandler>();
                                      builder.Register<FakeGlobalSyncCommandPostHandler>();

                                      // Fake Command Handlers
                                      builder.Register<FakeCommandSyncPreHandler>();
                                      builder.Register<FakeCommandHandler>();
                                      builder.Register<FakeCommandSyncHandler>();
                                      builder.Register<FakeCommandSyncPostHandler>();
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
        command.ExecutedTypes[0].Should().Be<FakeGlobalSyncCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<FakeCommandSyncPreHandler>();
        command.ExecutedTypes[2].Should().Be<FakeCommandSyncHandler>();
        command.ExecutedTypes[3].Should().Be<FakeCommandSyncPostHandler>();
        command.ExecutedTypes[4].Should().Be<FakeGlobalSyncCommandPostHandler>();
    }
    
    [Fact]
    public void Send_FakeCommandWithoutResult_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddCommands(builder =>
                                  {
                                      // Global Handlers
                                      builder.Register<FakeGlobalSyncCommandPreHandler>();
                                      builder.Register<FakeGlobalSyncCommandPostHandler>();

                                      // Fake Command Handlers
                                      builder.Register<FakeCommandWithoutResultSyncPreHandler>();
                                      builder.Register<FakeCommandWithoutResultCommandHandler>();
                                      builder.Register<FakeCommandWithoutResultSyncHandler>();
                                      builder.Register<FakeCommandWithoutResultSyncPostHandler>();
                                  });
                              })
                              .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new FakeCommandWithoutResult();

        // Act
        commandMediator.Send(command);

        // Assert
        command.ExecutedTypes.Should().HaveCount(5);
        command.ExecutedTypes[0].Should().Be<FakeGlobalSyncCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<FakeCommandWithoutResultSyncPreHandler>();
        command.ExecutedTypes[2].Should().Be<FakeCommandWithoutResultSyncHandler>();
        command.ExecutedTypes[3].Should().Be<FakeCommandWithoutResultSyncPostHandler>();
        command.ExecutedTypes[4].Should().Be<FakeGlobalSyncCommandPostHandler>();
    }
}