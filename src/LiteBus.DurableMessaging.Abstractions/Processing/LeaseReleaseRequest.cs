using System;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Describes the release of a lease whose dispatch the processor abandoned before it reached an outcome.
/// </summary>
/// <param name="MessageId">The identifier of the leased message.</param>
/// <param name="LeaseOwner">The worker name that currently owns the lease.</param>
/// <param name="LeaseGeneration">The fencing generation returned by the lease acquisition.</param>
public sealed record LeaseReleaseRequest(
    Guid MessageId,
    string LeaseOwner,
    long LeaseGeneration);
