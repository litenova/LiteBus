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
///     <para>
///         Tuning trade-offs: larger <see cref="BatchSize" /> values reduce store round-trips but increase the number of
///         leases held during dispatch and the blast radius when a worker stops mid-pass. Longer <see cref="LeaseDuration" />
///         values tolerate slow handlers but delay recovery when a worker crashes without releasing leases.
///     </para>
/// </remarks>
public sealed record InboxProcessorOptions
{
    /// <summary>
    ///     Gets the maximum number of envelopes leased per processing pass.
    /// </summary>
    /// <value>
    ///     The batch size. Default is <c>20</c> envelopes. Unit: count (messages), not bytes or time.
    ///     Values less than or equal to zero are rejected at processor construction.
    /// </value>
    /// <remarks>
    ///     Raising the value improves throughput when dispatch is fast and the store supports efficient batch leasing.
    ///     Lower it when handlers are slow, leases contend across nodes, or you want smaller failure domains per pass.
    /// </remarks>
    public int BatchSize { get; init; } = 20;

    /// <summary>
    ///     Gets the processing lease duration applied when the processor claims pending envelopes.
    /// </summary>
    /// <value>
    ///     Default is one minute. Unit: <see cref="TimeSpan" /> (wall-clock duration).
    /// </value>
    /// <remarks>
    ///     Set this longer than the expected handler execution time so another worker does not reclaim an envelope while
    ///     the first worker is still running. Setting it too high delays redelivery after a crash until the lease expires.
    /// </remarks>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Gets the retry settings used after dispatch failures.
    /// </summary>
    /// <value>
    ///     Default is a new <see cref="RetryOptions" /> instance (<see cref="RetryOptions.MaxAttempts" /> = 5,
    ///     <see cref="RetryOptions.InitialDelay" /> = 5 seconds, <see cref="RetryOptions.MaxDelay" /> = 5 minutes,
    ///     exponential backoff with jitter enabled).
    /// </value>
    /// <remarks>
    ///     The processor computes the next <c>visible_after</c> timestamp from these settings and dead-letters the envelope
    ///     when <see cref="RetryOptions.MaxAttempts" /> is exceeded. Aggressive retries can amplify load on failing dependencies.
    /// </remarks>
    public RetryOptions Retry { get; init; } = new();

    /// <summary>
    ///     Gets the optional lease owner name written on leased envelopes.
    /// </summary>
    /// <value>
    ///     <see langword="null" /> by default. When omitted, the processor creates a value from machine name, process id,
    ///     and a random suffix so multiple instances on one host remain distinguishable in storage diagnostics.
    /// </value>
    /// <remarks>
    ///     Supply a stable name when operators correlate leases in logs or SQL with a known deployment slot or pod name.
    /// </remarks>
    public string? LeaseOwner { get; init; }

    /// <summary>
    ///     Gets the number of parallel dispatch workers used by the pipelined inbox processor.
    /// </summary>
    /// <value>
    ///     Default is <c>1</c>, which serializes dispatch within a pass. Values less than or equal to zero are rejected
    ///     at processor construction.
    /// </value>
    /// <remarks>
    ///     Raise this value when handlers are I/O bound and the store supports guarded terminal writes. Keep it at
    ///     <c>1</c> when handlers must run strictly one at a time per processor instance.
    /// </remarks>
    public int DispatcherConcurrency { get; init; } = 1;

    /// <summary>
    ///     Gets the interval at which active leases are renewed while dispatch is in progress.
    /// </summary>
    /// <value>
    ///     Default is 15 seconds. Unit: <see cref="TimeSpan" /> (wall-clock duration). Set to
    ///     <see cref="TimeSpan.Zero" /> to disable heartbeat renewal.
    /// </value>
    /// <remarks>
    ///     Heartbeat renewal extends <c>lease_expires_at</c> for envelopes currently being dispatched so another worker
    ///     does not reclaim them during slow handlers. The interval should be shorter than
    ///     <see cref="LeaseDuration" />.
    /// </remarks>
    public TimeSpan LeaseHeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     Gets the optional tenant identifier that limits leasing to one tenant partition.
    /// </summary>
    /// <value>
    ///     <see langword="null" /> by default, which leases messages for all tenants. Set this when registering a
    ///     dedicated processor instance per tenant.
    /// </value>
    public string? TenantId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether terminal <c>PersistAsync</c> calls observe the shutdown or dispatch cancellation token.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> by default. When <see langword="false" />, processors pass
    ///     <c>CancellationToken.None</c> so a reclaimed lease cannot abort an in-flight terminal write.
    ///     When <see langword="true" />, the active cancellation token is passed for faster drain at the risk of
    ///     duplicate dispatch if shutdown cancels persist after handler execution.
    /// </value>
    public bool HonorShutdownTokenOnPersist { get; init; }
}
