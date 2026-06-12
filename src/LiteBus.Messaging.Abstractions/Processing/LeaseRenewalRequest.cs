using System;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Describes one lease renewal attempt for an in-flight processor message.
/// </summary>
/// <param name="MessageId">The identifier of the leased message.</param>
/// <param name="LeaseOwner">The worker name that currently owns the lease.</param>
/// <param name="ExpiresAt">The new UTC expiration timestamp written to storage.</param>
public sealed record LeaseRenewalRequest(Guid MessageId, string LeaseOwner, DateTimeOffset ExpiresAt);
