namespace LiteBus.Sample.ImmediateDispatch;

/// <summary>
///     Describes the state returned by the sample immediate order endpoint.
/// </summary>
/// <param name="OrderId">The order identifier.</param>
/// <param name="Amount">The placed amount.</param>
/// <param name="State">The current order state.</param>
public sealed record ImmediateOrderStatus(Guid OrderId, decimal Amount, string State);
