using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.MessageModule.UnitTests;

/// <summary>
/// Contains tests for polymorphic message dispatch, ensuring that handlers
/// for base message types can process derived message types.
/// </summary>
[Collection("Sequential")]
public sealed class PolymorphicDispatchTests : LiteBusTestBase
{
    // Test Components
    private interface IAuditableCommand
    {
        List<Type> ExecutedTypes { get; }
    }

    private abstract record BasePolymorphicCommand(List<Type> ExecutedTypes) : IAuditableCommand, ICommand;

    private sealed record SpecializedPolymorphicCommand(List<Type> ExecutedTypes) : BasePolymorphicCommand(ExecutedTypes);

    private sealed class BasePolymorphicCommandHandler : ICommandHandler<BasePolymorphicCommand>
    {
        public Task HandleAsync(BasePolymorphicCommand message, CancellationToken cancellationToken = default)
        {
            message.ExecutedTypes.Add(GetType());
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Send_SpecializedCommand_ShouldBeHandledByBaseCommandHandler()
    {
        // ARRANGE
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddCommandModule(builder =>
            {
                // Register only the handler for the BASE command.
                // LiteBus should be smart enough to route the specialized command to it.
                builder.Register<BasePolymorphicCommandHandler>();
            });
        }).BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new SpecializedPolymorphicCommand([]);

        // ACT
        await commandMediator.SendAsync(command);

        // ASSERT
        command.ExecutedTypes.Should().HaveCount(1);
        command.ExecutedTypes[0].Should().Be<BasePolymorphicCommandHandler>();
    }
}