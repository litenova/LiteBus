using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.UnitTests.Modules;

/// <summary>
///     Verifies durable adapter modules require composition through the axis core module.
/// </summary>
public sealed class AxisAdapterModuleRegistrationGuardTests
{
    [Fact]
    public void InMemoryInboxStorageModule_BuildWithoutInboxCore_ShouldThrow()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.Register(new InMemoryInboxStorageModule(_ =>
                {
                }));
            })
            .BuildServiceProvider();

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*AddInboxModule*");
    }

    [Fact]
    public void InMemoryOutboxStorageModule_BuildWithoutOutboxCore_ShouldThrow()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.Register(new InMemoryOutboxStorageModule(_ =>
                {
                }));
            })
            .BuildServiceProvider();

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*AddOutboxModule*");
    }
}
