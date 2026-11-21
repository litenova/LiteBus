using LiteBus.Commands.Abstractions;

namespace LiteBus.Samples.Documentation.Auditing;

/// <summary>
///     Cancels one order.
/// </summary>
/// <param name="OrderId">The identifier of the order to cancel.</param>
public sealed record CancelOrderCommand(Guid OrderId) : ICommand;
