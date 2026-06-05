using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
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
                .AddLiteBus(modules => modules.Register(new InMemoryInboxStorageModule(_ => { })))
                .BuildServiceProvider();
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*InboxModule*UseInMemoryStorage*");
    }

    [Fact]
    public void AddInboxModule_WithNestedStorageAndDispatcher_ShouldResolveInboxServices()
    {
        var services = new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                modules.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatcher();
                });
            });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IInbox>().Should().NotBeNull();
        provider.GetRequiredService<IInboxStore>().Should().NotBeNull();
        provider.GetRequiredService<IInboxDispatcher>().Should().BeOfType<InProcessInboxDispatcher>();
    }

    [Fact]
    public async Task NestedConfiguration_ShouldAcceptAndProcessCommand()
    {
        var recorder = new InboxTestFixtures.CommandRecorder();

        await using var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                modules.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatcher();
                    inbox.EnableInboxProcessor(options => options.PollInterval = TimeSpan.FromMilliseconds(25));
                });
            })
            .BuildServiceProvider();

        var inbox = provider.GetRequiredService<IInbox>();
        var orderId = Guid.NewGuid();

        await inbox.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        var processor = provider.GetRequiredService<IInboxProcessor>();
        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(1);

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }
}
