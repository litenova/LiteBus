using System;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Defines shared durable processor settings for inbox and outbox workers.
/// </summary>
/// <remarks>
///     Axis-specific option records inherit these members so batch, lease, retry, and concurrency tuning stay aligned
///     across processors.
/// </remarks>
public record ProcessorOptions
{
    /// <summary>
    ///     Gets the maximum number of messages leased per processing pass.
    /// </summary>
    /// <value>
    ///     The batch size. Default is <c>20</c> messages. Values less than or equal to zero are rejected at processor
    ///     construction.
    /// </value>
    public int BatchSize { get; init; } = 20;

    /// <summary>
    ///     Gets the processing lease duration applied when the processor claims pending messages.
    /// </summary>
    /// <value>
    ///     Default is one minute.
    /// </value>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Gets the retry settings used after dispatch failures.
    /// </summary>
    /// <value>
    ///     Default is a new <see cref="RetryOptions" /> instance.
    /// </value>
    public RetryOptions Retry { get; init; } = new();

    /// <summary>
    ///     Gets the optional lease owner name written on leased messages.
    /// </summary>
    /// <value>
    ///     <see langword="null" /> by default. When omitted, the processor creates a value from machine name, process id,
    ///     and a random suffix.
    /// </value>
    public string? LeaseOwner { get; init; }

    /// <summary>
    ///     Gets the number of parallel dispatch workers used by the pipelined processor.
    /// </summary>
    /// <value>
    ///     Default is <c>1</c>. Values less than or equal to zero are rejected at processor construction.
    /// </value>
    public int DispatcherConcurrency { get; init; } = 1;

    /// <summary>
    ///     Gets the interval at which active leases are renewed while dispatch is in progress.
    /// </summary>
    /// <value>
    ///     Default is 15 seconds. Set to <see cref="TimeSpan.Zero" /> to disable heartbeat renewal.
    /// </value>
    public TimeSpan LeaseHeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     Gets the optional tenant identifier that limits leasing to one tenant partition.
    /// </summary>
    /// <value>
    ///     <see langword="null" /> by default, which leases messages for all tenants.
    /// </value>
    public string? TenantId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether terminal <c>PersistAsync</c> calls observe the shutdown or dispatch cancellation
    ///     token.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> by default.
    /// </value>
    public bool HonorShutdownTokenOnPersist { get; init; }
}