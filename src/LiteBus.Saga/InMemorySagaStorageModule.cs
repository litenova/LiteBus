using LiteBus.Runtime.Abstractions;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Registers the in-memory saga store when explicitly selected.
/// </summary>
public sealed class InMemorySagaStorageModule : ISagaStorageModule
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ISagaStore),
            typeof(InMemorySagaStore),
            InstanceLifetime.Singleton));
    }
}
