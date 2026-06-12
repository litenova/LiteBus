using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.MessageModule.UnitTests;

public sealed class MessageContractRegistryTests
{
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
    public void GetContract_WhenAttributePresent_ShouldRequireExplicitRegistration()
    {
        var registry = new MessageContractRegistry();

        var act = () => registry.GetContract(typeof(AttributedCommand));

        act.Should().Throw<MessageContractNotRegisteredException>();
    }

    [Fact]
    public void TryGetContract_WhenAttributePresent_ShouldRegisterOnDemand()
    {
        var registry = new MessageContractRegistry();

        var contract = registry.TryGetContract(typeof(AttributedCommand));

        contract.Should().NotBeNull();
        contract!.Name.Should().Be("orders.commands.ship");
        registry.GetMessageType("orders.commands.ship", 2).Should().Be(typeof(AttributedCommand));
    }

    [Fact]
    public void Register_WhenSameTypeAndContractRegisteredTwice_ShouldBeIdempotent()
    {
        var registry = new MessageContractRegistry();

        registry.Register<OrderCreated>("order-created");

        var act = () => registry.Register<OrderCreated>("order-created");

        act.Should().NotThrow();
        registry.GetContract(typeof(OrderCreated)).Name.Should().Be("order-created");
        registry.GetMessageType("order-created", 1).Should().Be(typeof(OrderCreated));
    }

    [Fact]
    public void ApplyContracts_FromInboxAndOutboxBuilders_ShouldNotThrowForSameContract()
    {
        var inboxContracts = new MessageContractBuilder();
        var outboxContracts = new MessageContractBuilder();

        inboxContracts.Register<OrderCreated>("order-created");
        outboxContracts.Register<OrderCreated>("order-created");

        var registry = new MessageContractRegistry();

        var act = () =>
        {
            inboxContracts.ApplyTo(registry);
            outboxContracts.ApplyTo(registry);
        };

        act.Should().NotThrow();
        registry.GetContract(typeof(OrderCreated)).Version.Should().Be(1);
    }

    [MessageContract("orders.commands.ship", 2)]
    public sealed record AttributedCommand(Guid OrderId);

    private sealed record UnregisteredCommand;

    private sealed record ReplayCommand;

    private sealed record OrderCreated(Guid OrderId);
}