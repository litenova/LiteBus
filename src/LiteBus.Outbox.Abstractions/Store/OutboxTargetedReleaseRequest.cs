using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Defines a request to give one leased outbox message back to the outbox processor, visible from a given time.
/// </summary>
/// <param name="MessageId">The identifier of the leased message.</param>
/// <param name="LeaseOwner">The owner that holds the lease.</param>
/// <param name="LeaseGeneration">The lease generation the owner holds, which fences a late release.</param>
/// <param name="VisibleAfter">The time from which the processor may lease the message again.</param>
public sealed record OutboxTargetedReleaseRequest(
    Guid MessageId,
    string LeaseOwner,
    long LeaseGeneration,
    DateTimeOffset VisibleAfter);
