using FluentAssertions;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Extensions.MicrosoftDependencyInjection;
using LiteBus.Commands.UnitTests.UseCases;
using LiteBus.Commands.UnitTests.UseCases.CreateProduct;
using LiteBus.Commands.UnitTests.UseCases.LogActivity;
using LiteBus.Commands.UnitTests.UseCases.UpdateProduct;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Commands.UnitTests;

public sealed class CommandModuleTests
{
    [Fact]
    public async Task Send_CreateProductCommand_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddCommandModule(builder => { builder.RegisterFromAssembly(typeof(CreateProductCommand).Assembly); }); })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new CreateProductCommand();

        // Act
        var commandResult = await commandMediator.SendAsync(command);

        // Assert
        commandResult.CorrelationId.Should().Be(command.CorrelationId);
        command.ExecutedTypes.Should().HaveCount(6);

        command.ExecutedTypes[0].Should().Be<GlobalCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<CreateProductCommandHandlerPreHandler>();
        command.ExecutedTypes[2].Should().Be<CreateProductCommandHandler>();
        command.ExecutedTypes[3].Should().Be<CreateProductCommandHandlerPostHandler1>();
        command.ExecutedTypes[4].Should().Be<CreateProductCommandHandlerPostHandler2>();
        command.ExecutedTypes[5].Should().Be<GlobalCommandPostHandler>();
    }

    [Fact]
    public async Task Send_UpdateProductCommand_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddCommandModule(builder => { builder.RegisterFromAssembly(typeof(UpdateProductCommand).Assembly); }); })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new UpdateProductCommand();

        // Act
        await commandMediator.SendAsync(command);

        // Assert
        command.ExecutedTypes.Should().HaveCount(6);

        command.ExecutedTypes[0].Should().Be<GlobalCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<UpdateProductCommandHandlerPreHandler>();
        command.ExecutedTypes[2].Should().Be<UpdateProductCommandHandler>();
        command.ExecutedTypes[3].Should().Be<UpdateProductCommandHandlerPostHandler1>();
        command.ExecutedTypes[4].Should().Be<UpdateProductCommandHandlerPostHandler2>();
        command.ExecutedTypes[5].Should().Be<GlobalCommandPostHandler>();
    }

    [Fact]
    public async Task Send_LogActivityCommand_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddCommandModule(builder => { builder.RegisterFromAssembly(typeof(LogActivityCommand<>).Assembly); }); })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var command = new LogActivityCommand<ProductCreatedLogPayload>
        {
            Payload = new ProductCreatedLogPayload { ProductId = Guid.NewGuid() }
        };

        // Act
        await commandMediator.SendAsync(command);

        // Assert
        command.ExecutedTypes.Should().HaveCount(5);

        command.ExecutedTypes[0].Should().Be<GlobalCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<LogActivityCommandPreHandler<ProductCreatedLogPayload>>();
        command.ExecutedTypes[2].Should().Be<LogActivityCommandHandler<ProductCreatedLogPayload>>();
        command.ExecutedTypes[3].Should().Be<LogActivityCommandPostHandler<ProductCreatedLogPayload>>();
        command.ExecutedTypes[4].Should().Be<GlobalCommandPostHandler>();
    }
}