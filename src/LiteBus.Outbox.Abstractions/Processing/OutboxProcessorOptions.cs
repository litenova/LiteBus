using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Defines outbox processor settings.
/// </summary>
/// <remarks>
///     <para>
///         These options are read when the processor instance is created. Register separate processor instances with
///         different options when an application needs different publisher identities or batch sizes.
///     </para>
/// </remarks>
public sealed record OutboxProcessorOptions
{
    /// <summary>
    ///     Gets the maximum number of messages leased per processing pass. Larger batches reduce store round-trips but
    ///     hold more leases while dispatchers publish.
    /// </summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>
    ///     Gets the publication lease duration. Set this longer than the expected dispatch time so another publisher does
    ///     not reclaim the message while the first publisher is still running.
    /// </summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Gets the retry settings used after publication failures.
    /// </summary>
    public RetryOptions Retry { get; init; } = new();

    /// <summary>
    ///     Gets the optional lease owner name. When omitted, the processor creates a value from machine name, process id,
    ///     and a random suffix.
    /// </summary>
    public string? LeaseOwner { get; init; }
}
