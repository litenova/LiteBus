namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Maps saga type names to CLR state types used by <see cref="ISagaStore" /> serialization.
/// </summary>
public interface ISagaStateTypeRegistry
{
    /// <summary>
    ///     Registers a saga state type for the supplied saga type name.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="sagaTypeName">The saga type name, typically a message contract name.</param>
    void Register<TState>(string sagaTypeName)
        where TState : class, new();

    /// <summary>
    ///     Resolves the saga state type for a saga type name.
    /// </summary>
    /// <param name="sagaTypeName">The saga type name to resolve.</param>
    /// <returns>The registered state type, or <see langword="null" /> when the saga type is unknown.</returns>
    Type? Resolve(string sagaTypeName);
}