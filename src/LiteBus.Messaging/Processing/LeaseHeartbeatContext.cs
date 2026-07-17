using System;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace LiteBus.Messaging.Processing;

/// <summary>
///     Groups the lease renewal inputs shared by heartbeat helpers while processor dispatch work runs.
/// </summary>
/// <param name="MessageId">The identifier of the leased message.</param>
/// <param name="LeaseOwner">The worker name that currently owns the lease.</param>
/// <param name="LeaseGeneration">The fencing generation returned by lease acquisition.</param>
/// <param name="LeaseStore">The lease store used to extend ownership.</param>
/// <param name="LeaseDuration">The duration applied on each renewal from the current UTC time.</param>
/// <param name="HeartbeatInterval">The delay between renewal attempts after the initial renewal.</param>
/// <param name="Clock">The time provider used to compute renewal expirations.</param>
/// <param name="ProcessorName">The semantic processor name used in lease diagnostics.</param>
/// <param name="OnLeaseLost">An optional callback invoked when lease renewal fails.</param>
/// <param name="Logger">The optional logger used for lease-lost diagnostics.</param>
internal sealed record LeaseHeartbeatContext(
    Guid MessageId,
    string LeaseOwner,
    long LeaseGeneration,
    ILeaseRenewable LeaseStore,
    TimeSpan LeaseDuration,
    TimeSpan HeartbeatInterval,
    TimeProvider Clock,
    string ProcessorName,
    Action? OnLeaseLost = null,
    ILogger? Logger = null);
