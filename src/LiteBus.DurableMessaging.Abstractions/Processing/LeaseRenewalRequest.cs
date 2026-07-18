using System;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Describes one lease renewal attempt for an in-flight processor message.
/// </summary>
/// <param name="MessageId">The identifier of the leased message.</param>
/// <param name="LeaseOwner">The worker name that currently owns the lease.</param>
/// <param name="LeaseGeneration">The fencing generation returned by the lease acquisition.</param>
/// <param name="LeaseDuration">The duration added to the store's authoritative current time.</param>
/// <param name="RequestedExpiresAt">The fallback expiration used by stores without an authoritative database clock.</param>
public sealed record LeaseRenewalRequest(
    Guid MessageId,
    string LeaseOwner,
    long LeaseGeneration,
    TimeSpan LeaseDuration,
    DateTimeOffset RequestedExpiresAt);
