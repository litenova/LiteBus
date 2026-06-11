using LiteBus.Commands.Abstractions;

namespace LiteBus.Samples.V6.Saga;

/// <summary>
///     Command accepted into the inbox to advance correlated order saga state.
/// </summary>
/// <param name="OrderId">The order identifier.</param>
public sealed record AdvanceOrderSagaCommand(Guid OrderId) : ICommand;