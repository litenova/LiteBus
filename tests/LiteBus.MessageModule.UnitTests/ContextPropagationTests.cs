using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.MessageModule.UnitTests;

/// <summary>
///     Contains tests to verify that the AmbientExecutionContext.Items collection
///     correctly propagates data across all handlers in a single mediation pipeline.
/// </summary>
[Collection("Sequential")]
public sealed class ContextPropagationTests : LiteBusTestBase
{
    [Fact]
    public async Task Send_Command_ShouldPropagateContextItemsToAllHandlers()
    {
        // ARRANGE
        var serviceProvider = new ServiceCollection().AddLiteBus(configuration =>
        {
            configuration.AddCommandModule(builder =>
            {
                builder.Register<ContextPropagationPreHandler>();
                builder.Register<ContextPropagationCommandHandler>();
                builder.Register<ContextPropagationPostHandler>();
            });
        }).BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new ContextPropagationCommand();

        // ACT
        await commandMediator.SendAsync(command);

        // ASSERT
        // Both the main handler and post-handler should have been able to access the item set by the pre-handler.
        command.FoundContextItems.Should().HaveCount(2);
        command.FoundContextItems.Should().AllBe("ValueFromPreHandler");
    }

    // Test Components
    private sealed record ContextPropagationCommand : ICommand
    {
        public List<string> FoundContextItems { get; } = new();
    }

    private sealed class ContextPropagationPreHandler : ICommandPreHandler<ContextPropagationCommand>
    {
        public Task PreHandleAsync(ContextPropagationCommand message, CancellationToken cancellationToken = default)
        {
            AmbientExecutionContext.Current.Items["TestKey"] = "ValueFromPreHandler";
            return Task.CompletedTask;
        }
    }

    private sealed class ContextPropagationCommandHandler : ICommandHandler<ContextPropagationCommand>
    {
        public Task HandleAsync(ContextPropagationCommand message, CancellationToken cancellationToken = default)
        {
            if (AmbientExecutionContext.Current.Items.TryGetValue("TestKey", out var value))
            {
                message.FoundContextItems.Add((string) value);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ContextPropagationPostHandler : ICommandPostHandler<ContextPropagationCommand>
    {
        public Task PostHandleAsync(ContextPropagationCommand message, object? messageResult, CancellationToken cancellationToken = default)
        {
            if (AmbientExecutionContext.Current.Items.TryGetValue("TestKey", out var value))
            {
                message.FoundContextItems.Add((string) value);
            }

            return Task.CompletedTask;
        }
    }
}