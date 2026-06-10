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
    ///     Maps a saga state type to one contract or saga type name used during inbox dispatch.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="sagaTypeName">The saga type name, typically a message contract name.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Use <c>MapState&lt;TState&gt;(contractName)</c> inside <c>EnableSaga</c> callbacks. The internal
    ///     <see cref="ISagaStateTypeRegistry" /> stores the mapping; there is no public <c>Register&lt;TState&gt;</c> on
    ///     this builder.
    /// </remarks>
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
