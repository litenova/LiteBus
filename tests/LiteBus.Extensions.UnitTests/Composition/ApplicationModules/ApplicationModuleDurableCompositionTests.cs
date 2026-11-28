using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.UnitTests.Composition.ApplicationModules;

public sealed class ApplicationModuleDurableCompositionTests
{
    [Fact]
    public void TwoApplicationModules_EachAddingInbox_ShouldApplyBoth()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(builder =>
            {
                builder.AddMessaging(_ =>
                {
                });
                builder.AddApplicationModule<FirstInboxCompositionModule>();
                builder.AddApplicationModule<SecondInboxCompositionModule>();
            })
            .BuildServiceProvider();

        var contracts = provider.GetRequiredService<IMessageContractRegistry>();
        contracts.GetMessageType("test.inbox.first", 1).Should().Be<FirstInboxAppCommand>();
        contracts.GetMessageType("test.inbox.second", 1).Should().Be<SecondInboxAppCommand>();

        provider.GetRequiredService<IInbox>().Should().NotBeNull();
        provider.GetRequiredService<IInboxStore>().Should().NotBeNull();
    }
}

internal sealed record FirstInboxAppCommand : ICommand;

internal sealed record SecondInboxAppCommand : ICommand;

internal sealed class FirstInboxCompositionModule : IApplicationModule
{
    public void Configure(ILiteBusBuilder liteBus)
    {
        liteBus.AddInbox(inbox =>
        {
            inbox.Contracts.Register<FirstInboxAppCommand>("test.inbox.first");
            inbox.UseInMemoryStorage();
        });
    }
}

internal sealed class SecondInboxCompositionModule : IApplicationModule
{
    public void Configure(ILiteBusBuilder liteBus)
    {
        liteBus.AddInbox(inbox =>
        {
            inbox.Contracts.Register<SecondInboxAppCommand>("test.inbox.second");
        });
    }
}
