using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.MessageModule.UnitTests;

public sealed class MessageContractRegistryTests
{
    [MessageContract("orders.commands.ship", 2)]
    public sealed record AttributedCommand(Guid OrderId);

    [Fact]
    public void TryGetContract_WhenNotRegistered_ShouldReturnNull()
    {
        var registry = new MessageContractRegistry();

        registry.TryGetContract(typeof(UnregisteredCommand)).Should().BeNull();
    }

    [Fact]
    public void TryGetMessageType_WhenNotRegistered_ShouldReturnNull()
    {
        var registry = new MessageContractRegistry();

        registry.TryGetMessageType("missing.contract", 1).Should().BeNull();
    }

    [Fact]
    public void AddFromAssembly_ShouldRegisterAttributedClosedTypes()
    {
        var registry = new MessageContractRegistry();

        registry.AddFromAssembly(typeof(MessageContractRegistryTests).Assembly);

        var contract = registry.GetContract(typeof(AttributedCommand));
        contract.Name.Should().Be("orders.commands.ship");
        contract.Version.Should().Be(2);
    }

    [Fact]
    public void MessageContractBuilder_ShouldReplayRegistrationsToLiveRegistry()
    {
        var builder = new MessageContractBuilder();
        builder.Register<ReplayCommand>("orders.commands.replay", 3);

        var registry = new MessageContractRegistry();
        builder.ApplyTo(registry);

        registry.GetMessageType("orders.commands.replay", 3).Should().Be(typeof(ReplayCommand));
        builder.HasRegistrations.Should().BeTrue();
    }

    [Fact]
    public void GetContract_WhenAttributePresent_ShouldRegisterOnDemand()
    {
        var registry = new MessageContractRegistry();

        var contract = registry.GetContract(typeof(AttributedCommand));

        contract.Name.Should().Be("orders.commands.ship");
        registry.GetMessageType("orders.commands.ship", 2).Should().Be(typeof(AttributedCommand));
    }

    private sealed record UnregisteredCommand;

    private sealed record ReplayCommand;
}
