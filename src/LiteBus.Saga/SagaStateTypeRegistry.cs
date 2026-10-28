using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LiteBus.Runtime.Abstractions.Exceptions;
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
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type? ResolveStateType(string sagaDefinitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaDefinitionId);
        return _stateTypes.TryGetValue(sagaDefinitionId, out var stateType) ? stateType : null;
    }

    /// <summary>
    ///     Throws when a mapped contract names a saga definition with no registered state type.
    /// </summary>
    /// <exception cref="DurableStorageConfigurationException">
    ///     A contract mapping names a saga definition identifier with no registered state type.
    /// </exception>
    internal void ValidateContractMappings()
    {
        var missing = _contractMappings
            .Where(mapping => !_stateTypes.ContainsKey(mapping.Value))
            .OrderBy(mapping => mapping.Key, StringComparer.Ordinal)
            .Select(mapping => $"Saga contract '{mapping.Key}' maps to saga definition '{mapping.Value}', which has no registered state type.")
            .ToArray();

        if (missing.Length > 0)
        {
            throw new DurableStorageConfigurationException(string.Join(" ", missing));
        }
    }
}
