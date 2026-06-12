using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Default in-memory registry that maps saga definition identifiers and contracts to CLR state types.
/// </summary>
public sealed class SagaStateTypeRegistry : ISagaStateTypeRegistry
{
    /// <summary>
    ///     The saga definition identifiers mapped to state types.
    /// </summary>
    private readonly Dictionary<string, Type> _stateTypes = new(StringComparer.Ordinal);

    /// <summary>
    ///     The message contract names mapped to saga definition identifiers.
    /// </summary>
    private readonly Dictionary<string, string> _contractMappings = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void RegisterStateType<TState>(string sagaDefinitionId)
        where TState : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaDefinitionId);
        _stateTypes[sagaDefinitionId] = typeof(TState);
    }

    /// <inheritdoc />
    public void MapContract(string contractName, string sagaDefinitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaDefinitionId);
        _contractMappings[contractName] = sagaDefinitionId;
    }

    /// <inheritdoc />
    public string? ResolveDefinitionId(string contractName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        if (_contractMappings.TryGetValue(contractName, out var sagaDefinitionId))
        {
            return sagaDefinitionId;
        }

        return _stateTypes.ContainsKey(contractName) ? contractName : null;
    }

    /// <inheritdoc />
    public Type? ResolveStateType(string sagaDefinitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaDefinitionId);
        return _stateTypes.TryGetValue(sagaDefinitionId, out var stateType) ? stateType : null;
    }
}
