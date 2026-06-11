using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests;

public sealed class LiteBusBuilderTests
{
    [Fact]
    public void AddLiteBus_WithSharedContracts_ShouldRegisterContractsInResolvedRegistry()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(builder =>
        {
            builder.Contracts.Register<SharedContractMessage>("shared.contract", 2);

            builder.Modules.AddMessageModule(_ =>
            {
            });
        });

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IMessageContractRegistry>();

        registry.GetMessageType("shared.contract", 2).Should().Be(typeof(SharedContractMessage));
    }

    [Fact]
    public void AddLiteBus_WithSharedAndModuleContracts_ShouldApplyBothWithoutConflict()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(builder =>
        {
            builder.Contracts.Register<SharedContractMessage>("shared.contract", 2);

            builder.Modules.AddMessageModule(messaging =>
                messaging.Contracts.Register<ModuleContractMessage>("module.contract"));
        });

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IMessageContractRegistry>();

        registry.GetMessageType("shared.contract", 2).Should().Be(typeof(SharedContractMessage));
        registry.GetMessageType("module.contract", 1).Should().Be(typeof(ModuleContractMessage));
    }

    private sealed record SharedContractMessage(Guid Id);

    private sealed record ModuleContractMessage(string Name);
}