using LiteBus.Commands.Abstractions;

namespace LiteBus.Samples.Documentation.Orders;

/// <summary>
///     Requests placement of a new order through the command mediator.
/// </summary>
/// <param name="OrderId">The caller-supplied order identifier.</param>
/// <param name="Amount">The order total.</param>
public sealed record PlaceOrderCommand(Guid OrderId, decimal Amount) : ICommand;
