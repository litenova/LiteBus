using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Default in-memory registry that maps saga type names to CLR state types.
/// </summary>
public sealed class SagaStateTypeRegistry : ISagaStateTypeRegistry
{
    /// <summary>
    ///     The saga type names mapped to state types.
    /// </summary>
    private readonly Dictionary<string, Type> _stateTypes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Register<TState>(string sagaTypeName)
        where TState : class, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaTypeName);
        _stateTypes[sagaTypeName] = typeof(TState);
    }

    /// <inheritdoc />
    public Type? Resolve(string sagaTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaTypeName);
        return _stateTypes.TryGetValue(sagaTypeName, out var stateType) ? stateType : null;
    }
}