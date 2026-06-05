using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Configures saga state type registrations for inbox saga support.
/// </summary>
public sealed class SagaModuleBuilder
{
    /// <summary>
    ///     The saga state type registry populated by configuration callbacks.
    /// </summary>
    private readonly SagaStateTypeRegistry _registry = new();

    /// <summary>
    ///     Registers saga state for one contract or saga type name.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="sagaTypeName">The saga type name, typically a message contract name.</param>
    /// <returns>The current builder.</returns>
    public SagaModuleBuilder MapState<TState>(string sagaTypeName)
        where TState : class, new()
    {
        _registry.Register<TState>(sagaTypeName);
        return this;
    }

    /// <summary>
    ///     Collects the configured saga state type registry.
    /// </summary>
    /// <returns>The registry built from configuration callbacks.</returns>
    internal ISagaStateTypeRegistry CollectRegistry() => _registry;
}
