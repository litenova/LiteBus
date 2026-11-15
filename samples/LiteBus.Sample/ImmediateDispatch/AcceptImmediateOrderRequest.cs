namespace LiteBus.Sample.ImmediateDispatch;

/// <summary>
///     Carries the order fields accepted by the sample immediate order endpoint.
/// </summary>
/// <param name="OrderId">The caller-supplied order identifier.</param>
/// <param name="Amount">The positive order amount.</param>
public sealed record AcceptImmediateOrderRequest(Guid OrderId, decimal Amount);
