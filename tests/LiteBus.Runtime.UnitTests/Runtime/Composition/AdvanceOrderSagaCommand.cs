using LiteBus.Commands.Abstractions;

namespace LiteBus.Runtime.UnitTests.Runtime.Composition;

/// <summary>
///     Command accepted into the inbox to advance correlated order saga state in composition smoke tests.
/// </summary>
/// <param name="OrderId">The order identifier.</param>
public sealed record AdvanceOrderSagaCommand(Guid OrderId) : ICommand;
