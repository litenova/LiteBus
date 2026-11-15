using LiteBus.Messaging.Abstractions;

namespace LiteBus.Sample.ImmediateDispatch;

/// <summary>
///     Reports that an immediate order was placed, for dispatch right after the unit of work commits.
/// </summary>
/// <param name="OrderId">The placed order identifier.</param>
/// <param name="Amount">The placed order amount.</param>
[DispatchImmediately]
public sealed record OrderPlacedImmediately(Guid OrderId, decimal Amount);
