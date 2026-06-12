using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests;

public sealed class InboxCompositeModuleTests
{
    [Fact]
    public void InMemoryStorageModule_WhenRegisteredWithoutInboxCore_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(registry => registry.Register(new InMemoryInboxStorageModule(_ =>
                {
                })))
                .BuildServiceProvider();
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*requires*InboxModule*not registered*");
    }

    [Fact]
    public void AddInboxModule_WithoutStorage_ShouldThrowLiteBusConfigurationException()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(registry =>
                {
                    registry.AddMessageModule(_ =>
                    {
                    });

                    registry.AddInboxModule();
                })
                .BuildServiceProvider();
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*Inbox storage is required*");
    }

    [Fact]
    public void AddInboxModule_WithNestedStorageAndDispatcher_ShouldResolveInboxServices()
    {
        var services = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");
                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatch();
                });
            });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IInbox>().Should().NotBeNull();
        provider.GetRequiredService<IInboxStore>().Should().NotBeNull();
        provider.GetRequiredService<IInboxDispatcher>().Should().BeOfType<CommandInboxDispatcher>();
    }

    [Fact]
    public async Task NestedConfiguration_ShouldAcceptAndProcessCommand()
    {
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");
                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatch();
                    inbox.EnableInboxProcessor(options => options.PollInterval = TimeSpan.FromMilliseconds(25));
                });
            })
            .BuildServiceProvider();

        var inbox = provider.GetRequiredService<IInbox>();
        var orderId = Guid.NewGuid();

        await inbox.AcceptAsync(new InboxTestFixtures.ShipOrderCommand {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        var processor = provider.GetRequiredService<IInboxProcessor>();
        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(1);

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }
}