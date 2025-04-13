using FluentAssertions;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Extensions.MicrosoftDependencyInjection;
using LiteBus.Commands.UnitTests.UseCases;
using LiteBus.Commands.UnitTests.UseCases.CommandWithTag;
using LiteBus.Commands.UnitTests.UseCases.CreateProduct;
using LiteBus.Commands.UnitTests.UseCases.LogActivity;
using LiteBus.Commands.UnitTests.UseCases.ProblematicCommand;
using LiteBus.Commands.UnitTests.UseCases.UpdateProduct;
using LiteBus.Messaging.Abstractions;
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
        commandResult.Should().NotBeNull();
        commandResult!.CorrelationId.Should().Be(command.CorrelationId);
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

    [Fact]
    public async Task mediating_a_command_with_exception_in_pre_handler_goes_through_error_handlers()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddCommandModule(builder => { builder.RegisterFromAssembly(typeof(ProblematicCommandPreHandler).Assembly); }); })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var command = new ProblematicCommand
        {
            ThrowExceptionInType = typeof(ProblematicCommandPreHandler)
        };

        // Act
        await commandMediator.SendAsync(command);

        // Assert
        command.ExecutedTypes.Should().HaveCount(5);

        command.ExecutedTypes[0].Should().Be<GlobalCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<ProblematicCommandPreHandler>();
        command.ExecutedTypes[2].Should().Be<GlobalCommandErrorHandler>();
        command.ExecutedTypes[3].Should().Be<ProblematicCommandErrorHandler>();
        command.ExecutedTypes[4].Should().Be<ProblematicCommandErrorHandler2>();
    }

    [Fact]
    public async Task mediating_a_command_that_is_aborted_in_pre_handler_goes_through_correct_handlers()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddCommandModule(builder => { builder.RegisterFromAssembly(typeof(CreateProductCommand).Assembly); }); })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var command = new CreateProductCommand
        {
            AbortInPreHandler = true
        };

        // Act
        var commandResult = await commandMediator.SendAsync(command);

        // Assert
        commandResult.Should().NotBeNull();
        commandResult!.CorrelationId.Should().Be(Guid.Empty);
        command.ExecutedTypes.Should().HaveCount(2);

        command.ExecutedTypes[0].Should().Be<GlobalCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<CreateProductCommandHandlerPreHandler>();
    }

    [Fact]
    public async Task mediating_a_command_with_exception_in_post_global_handler_goes_through_error_handlers()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddCommandModule(builder => { builder.RegisterFromAssembly(typeof(ProblematicCommandPreHandler).Assembly); }); })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var command = new ProblematicCommand
        {
            ThrowExceptionInType = typeof(GlobalCommandPostHandler)
        };

        // Act
        await commandMediator.SendAsync(command);

        // Assert
        command.ExecutedTypes.Should().HaveCount(8);

        command.ExecutedTypes[0].Should().Be<GlobalCommandPreHandler>();
        command.ExecutedTypes[1].Should().Be<ProblematicCommandPreHandler>();
        command.ExecutedTypes[2].Should().Be<ProblematicCommandHandler>();
        command.ExecutedTypes[3].Should().Be<ProblematicCommandPostHandler>();
        command.ExecutedTypes[4].Should().Be<GlobalCommandPostHandler>();
        command.ExecutedTypes[5].Should().Be<GlobalCommandErrorHandler>();
        command.ExecutedTypes[6].Should().Be<ProblematicCommandErrorHandler>();
        command.ExecutedTypes[7].Should().Be<ProblematicCommandErrorHandler2>();
    }

    [Fact]
    public async Task mediating_an_command_with_specified_tag_goes_through_handlers_with_that_tag_and_handlers_without_any_tag_correctly()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddCommandModule(builder => { builder.RegisterFromAssembly(typeof(ProblematicCommandPreHandler).Assembly); }); })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var @command = new CommandWithTag();

        var settings = new CommandMediationSettings
        {
            Filters =
            {
                Tags = [Tags.Tag1]
            }
        };

        // Act
        await commandMediator.SendAsync(@command, settings);

        // Assert
        @command.ExecutedTypes.Should().HaveCount(7);
        @command.ExecutedTypes[0].Should().Be<GlobalCommandPreHandler>();
        @command.ExecutedTypes[1].Should().Be<CommandWithTagPreHandler1>();
        @command.ExecutedTypes[2].Should().Be<CommandWithTagPreHandler3>();
        @command.ExecutedTypes[3].Should().Be<CommandWithTagPreHandler4>();
        @command.ExecutedTypes[4].Should().Be<CommandWithTagHandler1>();
        @command.ExecutedTypes[5].Should().Be<CommandWithTagPostHandler1>();
        @command.ExecutedTypes[6].Should().Be<GlobalCommandPostHandler>();
    }

    [Fact]
    public async Task mediating_the_an_command_with_both_all_available_tags_will_fail_as_there_are_two_main_handlers()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration => { configuration.AddCommandModule(builder => { builder.RegisterFromAssembly(typeof(ProblematicCommandPreHandler).Assembly); }); })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var @command = new CommandWithTag();

        var settings = new CommandMediationSettings
        {
            Filters =
            {
                Tags = [Tags.Tag1, Tags.Tag2]
            }
        };

        // Act
        Func<Task> act = async () => await commandMediator.SendAsync(@command, settings);

        // Assert
        await act.Should().ThrowAsync<MultipleHandlerFoundException>();
    }
}