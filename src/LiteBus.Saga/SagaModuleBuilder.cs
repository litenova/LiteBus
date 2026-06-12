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
    ///     Registers a saga state type for one stable saga definition identifier.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="sagaDefinitionId">The saga definition identifier stored in durable saga rows.</param>
    /// <returns>The current builder.</returns>
    public SagaModuleBuilder DefineState<TState>(string sagaDefinitionId)
        where TState : class, new()
    {
        _registry.RegisterStateType<TState>(sagaDefinitionId);
        return this;
    }

    /// <summary>
    ///     Maps one message contract name to a saga definition identifier registered through
    ///     <see cref="DefineState{TState}" />.
    /// </summary>
    /// <param name="contractName">The durable message contract name.</param>
    /// <param name="sagaDefinitionId">The saga definition identifier that owns state for the contract.</param>
    /// <returns>The current builder.</returns>
    public SagaModuleBuilder MapContract(string contractName, string sagaDefinitionId)
    {
        _registry.MapContract(contractName, sagaDefinitionId);
        return this;
    }

    /// <summary>
    ///     Registers one saga state type and uses the same identifier for both definition and contract mapping.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="contractName">The contract name used as the saga definition identifier.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Equivalent to <c>DefineState&lt;TState&gt;(contractName)</c> for single-contract workflows.
    /// </remarks>
    public SagaModuleBuilder MapState<TState>(string contractName)
        where TState : class, new()
    {
        _registry.RegisterStateType<TState>(contractName);
        return this;
    }

    /// <summary>
    ///     Collects the configured saga state type registry.
    /// </summary>
    /// <returns>The registry built from configuration callbacks.</returns>
    internal ISagaStateTypeRegistry CollectRegistry()
    {
        return _registry;
    }
}
