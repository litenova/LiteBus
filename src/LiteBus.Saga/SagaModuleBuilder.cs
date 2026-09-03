using LiteBus.Saga.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

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
    ///     The single storage module owned by this saga composition.
    /// </summary>
    private ISagaStorageModule? _storageModule;

    /// <summary>
    ///     Gets a value indicating whether a saga store was selected explicitly.
    /// </summary>
    /// <value><see langword="true" /> when one storage module was registered.</value>
    public bool IsStorageConfigured => _storageModule is not null;

    /// <summary>
    ///     Registers a saga state type for one stable saga definition identifier.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="sagaDefinitionId">The saga definition identifier stored in durable saga rows.</param>
    /// <returns>The current builder.</returns>
    public SagaModuleBuilder RegisterState<TState>(string sagaDefinitionId)
        where TState : class, new()
    {
        return DefineState<TState>(sagaDefinitionId);
    }

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
    ///     Selects the in-memory saga store for tests and local hosts.
    /// </summary>
    /// <returns>The current builder.</returns>
    public SagaModuleBuilder UseInMemoryStorage()
    {
        return RegisterStorage(new InMemorySagaStorageModule());
    }

    /// <summary>
    ///     Registers the single storage module owned by this saga composition.
    /// </summary>
    /// <param name="storageModule">The saga storage module.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="DurableStorageConfigurationException">Thrown when storage was already selected.</exception>
    public SagaModuleBuilder RegisterStorage(ISagaStorageModule storageModule)
    {
        ArgumentNullException.ThrowIfNull(storageModule);

        if (_storageModule is not null)
        {
            throw new DurableStorageConfigurationException(
                "Saga storage is already configured. Select exactly one saga storage implementation.");
        }

        _storageModule = storageModule;
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

    /// <summary>
    ///     Collects the explicitly selected saga storage module.
    /// </summary>
    /// <returns>The selected storage module.</returns>
    /// <exception cref="DurableStorageConfigurationException">Thrown when no saga storage was selected.</exception>
    internal ISagaStorageModule CollectStorageModule()
    {
        return _storageModule ?? throw new DurableStorageConfigurationException(
            "Saga storage is required. Call UseInMemoryStorage or a storage adapter method such as UsePostgreSqlStorage inside EnableSaga(...).");
    }
}
