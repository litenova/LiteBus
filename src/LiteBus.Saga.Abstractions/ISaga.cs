namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Exposes mutable saga state to handlers participating in a correlated workflow.
/// </summary>
/// <typeparam name="TState">The saga state type.</typeparam>
/// <remarks>
///     Command handlers can implement this interface alongside <c>ICommandHandler&lt;TCommand&gt;</c> and mutate
///     <see cref="State" /> during dispatch. The inbox saga hook persists changes after successful dispatch.
/// </remarks>
public interface ISaga<TState>
    where TState : class, new()
{
    /// <summary>
    ///     Gets or sets the current saga state for the active correlation.
    /// </summary>
    TState State { get; set; }
}