using Autofac;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Autofac.UnitTests.UseCases;
using LiteBus.Testing;

namespace LiteBus.Extensions.Autofac.UnitTests;

/// <summary>
///     Contains tests to verify the LiteBus integration with an Autofac container.
///     These tests must run sequentially because they rely on the static MessageRegistry.
/// </summary>
[Collection("Sequential")]
public sealed class AutofacIntegrationTests : LiteBusTestBase
{
    [Fact]
    public async Task AddLiteBus_WithCommandModule_ResolvesAndExecutesHandlersCorrectly()
    {
        // ARRANGE
        var builder = new ContainerBuilder();

        // Configure LiteBus using the Autofac extension
        builder.AddLiteBus(configuration =>
        {
            configuration.AddCommandModule(commandModuleBuilder =>
            {
                commandModuleBuilder.Register<RegisterComponentCommand>();
                commandModuleBuilder.Register<RegisterComponentCommandPreHandler>();
                commandModuleBuilder.Register<RegisterComponentCommandHandler>();
                commandModuleBuilder.Register<RegisterComponentCommandPostHandler>();
            });
        });

        var container = builder.Build();
        var commandMediator = container.Resolve<ICommandMediator>();
        var command = new RegisterComponentCommand();

        // ACT
        await commandMediator.SendAsync(command);

        // ASSERT
        // Verify that all handlers were resolved from the Autofac container and executed in the correct order.
        command.ExecutedHandlers.Should().HaveCount(3);
        command.ExecutedHandlers[0].Should().Be<RegisterComponentCommandPreHandler>();
        command.ExecutedHandlers[1].Should().Be<RegisterComponentCommandHandler>();
        command.ExecutedHandlers[2].Should().Be<RegisterComponentCommandPostHandler>();
    }
}