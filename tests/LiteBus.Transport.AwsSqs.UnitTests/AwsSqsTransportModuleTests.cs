using AwesomeAssertions;
using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.AwsSqs.UnitTests;

/// <summary>
///     Verifies AWS SQS transport module registration behavior.
/// </summary>
public sealed class AwsSqsTransportModuleTests
{
    /// <summary>
    ///     Verifies the module registers transport services on first build.
    /// </summary>
    [Fact]
    public void Build_ShouldRegisterTransportServices()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        var options = new AwsSqsTransportOptions
        {
            Region = "us-east-1"
        };

        new AwsSqsTransportModule(options).Build(configuration);

        configuration.DependencyRegistry
            .Count(descriptor => descriptor.DependencyType == typeof(IMessageTransport))
            .Should()
            .Be(1);
    }

    /// <summary>
    ///     Verifies a second transport module throws instead of silently no-oping.
    /// </summary>
    [Fact]
    public void Build_SecondTransportModule_ShouldThrow()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());

        new InMemoryTransportModule().Build(configuration);

        var options = new AwsSqsTransportOptions { Region = "us-east-1" };

        var act = () => new AwsSqsTransportModule(options).Build(configuration);

        act.Should().Throw<Transport.TransportAlreadyRegisteredException>();
    }
}
