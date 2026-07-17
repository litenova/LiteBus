using LiteBus.Runtime.Dependencies;
using LiteBus.Runtime.Modules;
using LiteBus.Transport.InMemory;

namespace LiteBus.Transport.UnitTests.InMemory;

/// <summary>
///     Verifies in-memory transport module registration and validation.
/// </summary>
public sealed class InMemoryTransportModuleTests
{
    /// <summary>
    ///     Verifies configured destination capacity is registered for broker construction.
    /// </summary>
    [Fact]
    public void Build_WithConfiguredCapacity_ShouldRegisterOptions()
    {
        var configuration = new ModuleConfiguration(new DependencyRegistry());
        var options = new InMemoryTransportOptions { DestinationCapacity = 17 };

        new InMemoryTransportModule(options).Build(configuration);

        configuration.DependencyRegistry.Should().ContainSingle(descriptor =>
            descriptor.DependencyType == typeof(InMemoryTransportOptions) &&
            ReferenceEquals(descriptor.Instance, options));
    }

    /// <summary>
    ///     Verifies a non-positive destination capacity fails during module composition.
    /// </summary>
    [Fact]
    public void Constructor_WithNonPositiveDestinationCapacity_ShouldThrow()
    {
        var act = () => new InMemoryTransportModule(
            new InMemoryTransportOptions { DestinationCapacity = 0 });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
