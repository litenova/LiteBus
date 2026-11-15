using LiteBus.Commands.Abstractions;

namespace LiteBus.Sample.ImmediateDispatch;

/// <summary>
///     Requests placement of one order whose event is dispatched immediately after the commit.
/// </summary>
/// <param name="OrderId">The order identifier.</param>
/// <param name="Amount">The order amount.</param>
public sealed record PlaceImmediateOrderCommand(Guid OrderId, decimal Amount) : ICommand;
