namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Provides access to the active saga state for the envelope currently being dispatched.
/// </summary>
public interface ISagaContext
{
    /// <summary>
    ///     Gets a value indicating whether a saga scope is active for the current dispatch.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    ///     Gets the correlation for the active saga scope.
    /// </summary>
    SagaCorrelation? Correlation { get; }

    /// <summary>
    ///     Gets the current saga state deserialized for the active correlation.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <returns>The current state instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no saga scope is active.</exception>
    TState GetState<TState>()
        where TState : class, new();

    /// <summary>
    ///     Replaces the current saga state for the active correlation.
    /// </summary>
    /// <typeparam name="TState">The saga state type.</typeparam>
    /// <param name="state">The updated state snapshot.</param>
    void SetState<TState>(TState state)
        where TState : class, new();

    /// <summary>
    ///     Marks the active saga completed after dispatch succeeds.
    /// </summary>
    void Complete();
}