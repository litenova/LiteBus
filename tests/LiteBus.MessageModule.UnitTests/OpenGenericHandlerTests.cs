using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.MessageModule.UnitTests;

/// <summary>
///     Integration tests for open generic handler resolution.
///     Types are defined inline to avoid being picked up by RegisterFromAssembly in other test projects.
/// </summary>
[Collection("Sequential")]
public sealed class OpenGenericHandlerTests : LiteBusTestBase
{
    // --- Test Types ---

    public interface IAuditableCommand
    {
        List<Type> ExecutedTypes { get; }
    }

    public sealed class SimpleCommand : IAuditableCommand, ICommand
    {
        public List<Type> ExecutedTypes { get; } = new();
    }

    public sealed class AnotherSimpleCommand : IAuditableCommand, ICommand
    {
        public List<Type> ExecutedTypes { get; } = new();
    }

    public sealed class SimpleCommandHandler : ICommandHandler<SimpleCommand>
    {
        public Task HandleAsync(SimpleCommand message, CancellationToken cancellationToken = default)
        {
            message.ExecutedTypes.Add(GetType());
            return Task.CompletedTask;
        }
    }

    public sealed class AnotherSimpleCommandHandler : ICommandHandler<AnotherSimpleCommand>
    {
        public Task HandleAsync(AnotherSimpleCommand message, CancellationToken cancellationToken = default)
        {
            message.ExecutedTypes.Add(GetType());
            return Task.CompletedTask;
        }
    }

    public sealed class OpenGenericPreHandler<TCommand> : ICommandPreHandler<TCommand>
        where TCommand : ICommand
    {
        public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
        {
            if (message is IAuditableCommand auditableCommand)
            {
                auditableCommand.ExecutedTypes.Add(typeof(OpenGenericPreHandler<TCommand>));
            }

            return Task.CompletedTask;
        }
    }

    public sealed class OpenGenericPostHandler<TCommand> : ICommandPostHandler<TCommand>
        where TCommand : ICommand
    {
        public Task PostHandleAsync(TCommand message, object? messageResult, CancellationToken cancellationToken = default)
        {
            if (message is IAuditableCommand auditableCommand)
            {
                auditableCommand.ExecutedTypes.Add(typeof(OpenGenericPostHandler<TCommand>));
            }

            return Task.CompletedTask;
        }
    }

    // --- Tests ---

    [Fact]
    public async Task Send_WithOpenGenericPreHandler_ShouldExecuteForConcreteCommand()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register(typeof(OpenGenericPreHandler<>));
                    builder.Register<SimpleCommandHandler>();
                    builder.Register<SimpleCommand>();
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new SimpleCommand();

        // Act
        await commandMediator.SendAsync(command);

        // Assert
        command.ExecutedTypes.Should().HaveCount(2);
        command.ExecutedTypes[0].Should().Be<OpenGenericPreHandler<SimpleCommand>>();
        command.ExecutedTypes[1].Should().Be<SimpleCommandHandler>();
    }

    [Fact]
    public async Task Send_WithOpenGenericPreHandler_ShouldApplyToMultipleCommandTypes()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register(typeof(OpenGenericPreHandler<>));
                    builder.Register<SimpleCommandHandler>();
                    builder.Register<SimpleCommand>();
                    builder.Register<AnotherSimpleCommandHandler>();
                    builder.Register<AnotherSimpleCommand>();
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var command1 = new SimpleCommand();
        var command2 = new AnotherSimpleCommand();

        // Act
        await commandMediator.SendAsync(command1);
        await commandMediator.SendAsync(command2);

        // Assert
        command1.ExecutedTypes.Should().HaveCount(2);
        command1.ExecutedTypes[0].Should().Be<OpenGenericPreHandler<SimpleCommand>>();
        command1.ExecutedTypes[1].Should().Be<SimpleCommandHandler>();

        command2.ExecutedTypes.Should().HaveCount(2);
        command2.ExecutedTypes[0].Should().Be<OpenGenericPreHandler<AnotherSimpleCommand>>();
        command2.ExecutedTypes[1].Should().Be<AnotherSimpleCommandHandler>();
    }

    [Fact]
    public async Task Send_WithOpenGenericPostHandler_ShouldExecuteForConcreteCommand()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register(typeof(OpenGenericPostHandler<>));
                    builder.Register<SimpleCommandHandler>();
                    builder.Register<SimpleCommand>();
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new SimpleCommand();

        // Act
        await commandMediator.SendAsync(command);

        // Assert
        command.ExecutedTypes.Should().HaveCount(2);
        command.ExecutedTypes[0].Should().Be<SimpleCommandHandler>();
        command.ExecutedTypes[1].Should().Be<OpenGenericPostHandler<SimpleCommand>>();
    }

    [Fact]
    public async Task Send_WithOpenGenericPreAndPostHandler_ShouldExecuteInCorrectOrder()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register(typeof(OpenGenericPreHandler<>));
                    builder.Register(typeof(OpenGenericPostHandler<>));
                    builder.Register<SimpleCommandHandler>();
                    builder.Register<SimpleCommand>();
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new SimpleCommand();

        // Act
        await commandMediator.SendAsync(command);

        // Assert
        command.ExecutedTypes.Should().HaveCount(3);
        command.ExecutedTypes[0].Should().Be<OpenGenericPreHandler<SimpleCommand>>();
        command.ExecutedTypes[1].Should().Be<SimpleCommandHandler>();
        command.ExecutedTypes[2].Should().Be<OpenGenericPostHandler<SimpleCommand>>();
    }

    [Fact]
    public async Task Send_OpenGenericRegisteredBeforeCommand_ShouldStillApply()
    {
        // Arrange - register open generic BEFORE registering the command and handler
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register(typeof(OpenGenericPreHandler<>));
                    builder.Register<SimpleCommand>();
                    builder.Register<SimpleCommandHandler>();
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new SimpleCommand();

        // Act
        await commandMediator.SendAsync(command);

        // Assert
        command.ExecutedTypes.Should().HaveCount(2);
        command.ExecutedTypes[0].Should().Be<OpenGenericPreHandler<SimpleCommand>>();
        command.ExecutedTypes[1].Should().Be<SimpleCommandHandler>();
    }

    [Fact]
    public async Task Send_OpenGenericRegisteredAfterCommand_ShouldStillApply()
    {
        // Arrange - register the command and handler BEFORE the open generic
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<SimpleCommand>();
                    builder.Register<SimpleCommandHandler>();
                    builder.Register(typeof(OpenGenericPreHandler<>));
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new SimpleCommand();

        // Act
        await commandMediator.SendAsync(command);

        // Assert
        command.ExecutedTypes.Should().HaveCount(2);
        command.ExecutedTypes[0].Should().Be<OpenGenericPreHandler<SimpleCommand>>();
        command.ExecutedTypes[1].Should().Be<SimpleCommandHandler>();
    }
}
