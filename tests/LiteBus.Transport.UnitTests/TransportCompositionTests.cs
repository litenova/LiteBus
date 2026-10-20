using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Amqp;
using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Verifies one composition hosts one transport module.
/// </summary>
public sealed class TransportCompositionTests
{
    /// <summary>
    ///     Verifies composing two transport modules fails naming both modules.
    /// </summary>
    [Fact]
    public void Build_TwoTransportModules_ShouldThrowNamingBothModules()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new InMemoryTransportModule().Build(configuration);

        var act = () => new AmqpTransportModule(new AmqpConnectionOptions()).Build(configuration);

        var exception = act.Should().Throw<ModuleCompositionException>().Which;

        exception.Message.Should().Contain(typeof(InMemoryTransportModule).FullName!);
        exception.Message.Should().Contain(typeof(AmqpTransportModule).FullName!);
        exception.Message.Should().Contain("One composition hosts one transport.");
        exception.Message.Should().NotContain(nameof(TransportBrokerIdentity));
    }

    /// <summary>
    ///     Verifies a second metrics registration from another module type fails naming both callers.
    /// </summary>
    [Fact]
    public void RegisterIfNeeded_SecondModuleType_ShouldThrowNamingBothCallers()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        TransportMetricsRegistration.RegisterIfNeeded(
            configuration,
            "first",
            typeof(FirstTransportModuleStub));

        var act = () => TransportMetricsRegistration.RegisterIfNeeded(
            configuration,
            "second",
            typeof(SecondTransportModuleStub));

        var exception = act.Should().Throw<ModuleCompositionException>().Which;

        exception.Message.Should().Contain(nameof(FirstTransportModuleStub));
        exception.Message.Should().Contain(nameof(SecondTransportModuleStub));
        exception.Message.Should().NotContain(nameof(TransportBrokerIdentity));
    }

    /// <summary>
    ///     Stands in for the first transport module claiming a composition.
    /// </summary>
    private sealed class FirstTransportModuleStub : IModule
    {
        /// <inheritdoc />
        public void Build(IModuleConfiguration configuration)
        {
        }
    }

    /// <summary>
    ///     Stands in for a second transport module joining the same composition.
    /// </summary>
    private sealed class SecondTransportModuleStub : IModule
    {
        /// <inheritdoc />
        public void Build(IModuleConfiguration configuration)
        {
        }
    }
}
