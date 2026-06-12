using System.Diagnostics.CodeAnalysis;

namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Maps saga definition identifiers and message contract names to CLR state types.
/// </summary>
public interface ISagaStateTypeRegistry
{
    /// <summary>
    ///     Registers a saga state type for the supplied saga definition identifier.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="sagaDefinitionId">The stable saga definition identifier.</param>
    void RegisterStateType<TState>(string sagaDefinitionId)
        where TState : class, new();

    /// <summary>
    ///     Maps one message contract name to a saga definition identifier.
    /// </summary>
    /// <param name="contractName">The durable message contract name.</param>
    /// <param name="sagaDefinitionId">The saga definition identifier that owns state for the contract.</param>
    void MapContract(string contractName, string sagaDefinitionId);

    /// <summary>
    ///     Resolves the saga definition identifier for a message contract name.
    /// </summary>
    /// <param name="contractName">The durable message contract name.</param>
    /// <returns>
    ///     The mapped saga definition identifier, or <see langword="null" /> when the contract is not mapped.
    /// </returns>
    string? ResolveDefinitionId(string contractName);

    /// <summary>
    ///     Resolves the saga state type for a saga definition identifier.
    /// </summary>
    /// <param name="sagaDefinitionId">The saga definition identifier to resolve.</param>
    /// <returns>The registered state type, or <see langword="null" /> when the definition is unknown.</returns>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    Type? ResolveStateType(string sagaDefinitionId);
}
