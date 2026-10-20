using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

/// <summary>
///     Verifies handler-missing exceptions name the runtime message type even when the call erased it to a base
///     contract through a non-generic overload.
/// </summary>
[Collection("Sequential")]
public sealed class HandlerMissingMessageTypeTests : LiteBusTestBase
{
    [Fact]
    public async Task Send_ErasedCommandWithNoHandler_ShouldNameRuntimeMessageType()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                builder.Register<OrphanCommand>();
            });
        }).BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var act = () => commandMediator.SendAsync((ICommand) new OrphanCommand());

        var exception = await act.Should().ThrowAsync<NoHandlerFoundException>().ConfigureAwait(false);

        exception.Which.MessageType.Should().Be<OrphanCommand>();
        exception.Which.Message.Should().Contain("OrphanCommand");
    }

    [Fact]
    public async Task Send_ErasedCommandWithTwoHandlers_ShouldNameRuntimeMessageType()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                builder.Register<FirstOrphanCommandHandler>();
                builder.Register<SecondOrphanCommandHandler>();
            });
        }).BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var act = () => commandMediator.SendAsync((ICommand) new OrphanCommand());

        var exception = await act.Should().ThrowAsync<MultipleHandlerFoundException>().ConfigureAwait(false);

        exception.Which.MessageType.Should().Be<OrphanCommand>();
        exception.Which.Message.Should().Contain("OrphanCommand");
    }

    [Fact]
    public async Task Publish_ErasedEventWithNoHandler_ShouldNameRuntimeMessageType()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddEvents(builder =>
            {
                builder.Register<OrphanEvent>();
            });
        }).BuildServiceProvider();

        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var settings = new EventMediationSettings { ThrowIfNoHandlerFound = true };

        var act = () => eventMediator.PublishAsync((IEvent) new OrphanEvent(), settings);

        var exception = await act.Should().ThrowAsync<NoHandlerFoundException>().ConfigureAwait(false);

        exception.Which.MessageType.Should().Be<OrphanEvent>();
        exception.Which.Message.Should().Contain("OrphanEvent");
    }

    private sealed record OrphanCommand : ICommand;

    private sealed class FirstOrphanCommandHandler : ICommandHandler<OrphanCommand>
    {
        public Task HandleAsync(OrphanCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SecondOrphanCommandHandler : ICommandHandler<OrphanCommand>
    {
        public Task HandleAsync(OrphanCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed record OrphanEvent : IEvent;
}
