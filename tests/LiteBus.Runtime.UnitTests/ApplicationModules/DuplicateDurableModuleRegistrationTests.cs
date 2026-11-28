using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests.ApplicationModules;

public sealed class DuplicateDurableModuleRegistrationTests
{
    [Fact]
    public void AddInbox_Twice_ShouldApplyBothContractsAndServices()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(builder =>
            {
                builder.AddMessaging(_ =>
                {
                });

                builder.AddCommands(commands =>
                {
                    commands.Register<FirstInboxCommand>();
                    commands.Register<SecondInboxCommand>();
                });

                builder.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<FirstInboxCommand>("test.inbox.first");
                    inbox.UseInMemoryStorage();
                });

                builder.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<SecondInboxCommand>("test.inbox.second");
                    inbox.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var contracts = provider.GetRequiredService<IMessageContractRegistry>();
        contracts.GetMessageType("test.inbox.first", 1).Should().Be<FirstInboxCommand>();
        contracts.GetMessageType("test.inbox.second", 1).Should().Be<SecondInboxCommand>();

        provider.GetRequiredService<IInbox>().Should().NotBeNull();
        provider.GetRequiredService<IInboxStore>().Should().NotBeNull();
        provider.GetRequiredService<IInboxDispatcher>().Should().NotBeNull();
    }

    [Fact]
    public void AddOutbox_Twice_ShouldApplyBothContractsAndServices()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(builder =>
            {
                builder.AddMessaging(_ =>
                {
                });

                builder.AddEvents(events =>
                {
                    events.Register<FirstOutboxEvent>();
                    events.Register<SecondOutboxEvent>();
                });

                builder.AddOutbox(outbox =>
                {
                    outbox.Contracts.Register<FirstOutboxEvent>("test.outbox.first");
                    outbox.UseInMemoryStorage();
                });

                builder.AddOutbox(outbox =>
                {
                    outbox.Contracts.Register<SecondOutboxEvent>("test.outbox.second");
                    outbox.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var contracts = provider.GetRequiredService<IMessageContractRegistry>();
        contracts.GetMessageType("test.outbox.first", 1).Should().Be<FirstOutboxEvent>();
        contracts.GetMessageType("test.outbox.second", 1).Should().Be<SecondOutboxEvent>();

        provider.GetRequiredService<IOutbox>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxDispatcher>().Should().NotBeNull();
    }

    [Fact]
    public void AddInboxModule_Twice_ShouldConfigureOneModule()
    {
        var registry = new ModuleRegistry();
        registry.AddMessageModule(_ =>
        {
        });
        registry.AddCommandModule(commands =>
        {
            commands.Register<FirstInboxCommand>();
            commands.Register<SecondInboxCommand>();
        });
        registry.AddInboxModule(inbox =>
        {
            inbox.Contracts.Register<FirstInboxCommand>("test.inbox.first");
            inbox.UseInMemoryStorage();
        });
        registry.AddInboxModule(inbox =>
        {
            inbox.Contracts.Register<SecondInboxCommand>("test.inbox.second");
            inbox.UseInProcessDispatch();
        });

        registry.Find<InboxModule>().Should().NotBeNull();
        registry.BuildOrder().Count(descriptor => descriptor.ModuleType == typeof(InboxModule)).Should().Be(1);
    }

    [Fact]
    public void AddOutboxModule_Twice_ShouldConfigureOneModule()
    {
        var registry = new ModuleRegistry();
        registry.AddMessageModule(_ =>
        {
        });
        registry.AddEventModule(events =>
        {
            events.Register<FirstOutboxEvent>();
            events.Register<SecondOutboxEvent>();
        });
        registry.AddOutboxModule(outbox =>
        {
            outbox.Contracts.Register<FirstOutboxEvent>("test.outbox.first");
            outbox.UseInMemoryStorage();
        });
        registry.AddOutboxModule(outbox =>
        {
            outbox.Contracts.Register<SecondOutboxEvent>("test.outbox.second");
            outbox.UseInProcessDispatch();
        });

        registry.Find<OutboxModule>().Should().NotBeNull();
        registry.BuildOrder().Count(descriptor => descriptor.ModuleType == typeof(OutboxModule)).Should().Be(1);
    }

    [Fact]
    public void AddInboxModule_TwiceWithStorage_ShouldThrow()
    {
        var registry = new ModuleRegistry();
        registry.AddMessageModule(_ =>
        {
        });
        registry.AddInboxModule(inbox => inbox.UseInMemoryStorage());

        var act = () => registry.AddInboxModule(inbox => inbox.UseInMemoryStorage());

        act.Should().Throw<DurableStorageConfigurationException>().WithMessage("*already configured*");
    }

    [Fact]
    public void AddOutboxModule_TwiceWithStorage_ShouldThrow()
    {
        var registry = new ModuleRegistry();
        registry.AddMessageModule(_ =>
        {
        });
        registry.AddOutboxModule(outbox => outbox.UseInMemoryStorage());

        var act = () => registry.AddOutboxModule(outbox => outbox.UseInMemoryStorage());

        act.Should().Throw<DurableStorageConfigurationException>().WithMessage("*already configured*");
    }

    private sealed record FirstInboxCommand : ICommand;

    private sealed record SecondInboxCommand : ICommand;

    private sealed record FirstOutboxEvent : IEvent;

    private sealed record SecondOutboxEvent : IEvent;
}
