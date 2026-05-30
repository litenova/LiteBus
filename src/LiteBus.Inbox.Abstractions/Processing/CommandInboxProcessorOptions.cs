using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Defines inbox processor settings.
/// </summary>
/// <remarks>
///     <para>
///         These options are read when the processor instance is created. Register separate processor instances with
///         different options when an application needs different worker identities or batch sizes.
///     </para>
/// </remarks>
public sealed record InboxProcessorOptions
{
    /// <summary>
    ///     Gets the maximum number of envelopes leased per processing pass. Larger batches reduce store round-trips but
    ///     hold more leases while dispatch runs.
    /// </summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>
    ///     Gets the processing lease duration. Set this longer than the expected dispatch time so another worker does not
    ///     reclaim the envelope while the first worker is still running.
    /// </summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Gets the retry settings used after dispatch failures.
    /// </summary>
    public RetryOptions Retry { get; init; } = new();

    /// <summary>
    ///     Gets the optional lease owner name. When omitted, the processor creates a value from machine name, process id,
    ///     and a random suffix.
    /// </summary>
    public string? LeaseOwner { get; init; }
}
