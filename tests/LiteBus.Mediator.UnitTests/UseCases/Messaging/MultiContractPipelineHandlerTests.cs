using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

/// <summary>
///     Verifies pipeline invocation when one handler implements contracts for multiple message types.
/// </summary>
[Collection("Sequential")]
public sealed class MultiContractPipelineHandlerTests : LiteBusTestBase
{
    [Fact]
    public async Task Send_with_one_pre_and_post_handler_for_two_commands_uses_the_matching_methods()
    {
        using var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(builder =>
                {
                    builder.Register<MultiContractPipelineHandler>();
                    builder.Register<MultiContractCommandAHandler>();
                    builder.Register<MultiContractCommandBHandler>();
                    builder.Register<MultiContractCommandA>();
                    builder.Register<MultiContractCommandB>();
                });
            })
            .BuildServiceProvider();

        var mediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var commandA = new MultiContractCommandA();
        var commandB = new MultiContractCommandB();

        await mediator.SendAsync(commandA).ConfigureAwait(false);
        await mediator.SendAsync(commandB).ConfigureAwait(false);

        commandA.PreHandled.Should().BeTrue();
        commandA.Handled.Should().BeTrue();
        commandA.PostHandled.Should().BeTrue();
        commandB.PreHandled.Should().BeTrue();
        commandB.Handled.Should().BeTrue();
        commandB.PostHandled.Should().BeTrue();
    }

    private sealed class MultiContractPipelineHandler :
        ICommandPreHandler<MultiContractCommandA>,
        ICommandPreHandler<MultiContractCommandB>,
        ICommandPostHandler<MultiContractCommandA>,
        ICommandPostHandler<MultiContractCommandB>
    {
        object IMessagePreHandler.PreHandle(object message)
        {
            return message switch
            {
                MultiContractCommandA command => PreHandleAsync(command),
                MultiContractCommandB command => PreHandleAsync(command),
                _ => throw new ArgumentException("Unsupported command type.", nameof(message))
            };
        }

        object IMessagePostHandler.PostHandle(object message, object? messageResult)
        {
            return message switch
            {
                MultiContractCommandA command => PostHandleAsync(command, messageResult),
                MultiContractCommandB command => PostHandleAsync(command, messageResult),
                _ => throw new ArgumentException("Unsupported command type.", nameof(message))
            };
        }

        public Task PreHandleAsync(
            MultiContractCommandA message,
            CancellationToken cancellationToken = default)
        {
            message.PreHandled = true;
            return Task.CompletedTask;
        }

        public Task PreHandleAsync(
            MultiContractCommandB message,
            CancellationToken cancellationToken = default)
        {
            message.PreHandled = true;
            return Task.CompletedTask;
        }

        public Task PostHandleAsync(
            MultiContractCommandA message,
            object? messageResult,
            CancellationToken cancellationToken = default)
        {
            message.PostHandled = true;
            return Task.CompletedTask;
        }

        public Task PostHandleAsync(
            MultiContractCommandB message,
            object? messageResult,
            CancellationToken cancellationToken = default)
        {
            message.PostHandled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class MultiContractCommandAHandler : ICommandHandler<MultiContractCommandA>
    {
        public Task HandleAsync(MultiContractCommandA message, CancellationToken cancellationToken = default)
        {
            message.Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class MultiContractCommandBHandler : ICommandHandler<MultiContractCommandB>
    {
        public Task HandleAsync(MultiContractCommandB message, CancellationToken cancellationToken = default)
        {
            message.Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed record MultiContractCommandA : ICommand
    {
        public bool PreHandled { get; set; }

        public bool Handled { get; set; }

        public bool PostHandled { get; set; }
    }

    private sealed record MultiContractCommandB : ICommand
    {
        public bool PreHandled { get; set; }

        public bool Handled { get; set; }

        public bool PostHandled { get; set; }
    }
}
