using System;

namespace LiteBus.Auditing.Checkpoints;

/// <summary>
///     How often the checkpoint background service publishes pending checkpoints and cuts due ones.
/// </summary>
/// <remarks>
///     The service publishes every stored checkpoint still waiting for publication, then cuts a checkpoint for every
///     segment whose policy reports one due. A pass that fails waits with backoff instead of the poll interval, so a
///     store outage does not spin the loop.
/// </remarks>
public sealed class CheckpointPublishHostOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether the checkpoint publish and cut loop runs in this host.
    /// </summary>
    /// <value>The default is <see langword="true" />.</value>
    /// <remarks>
    ///     Turn it off in every host but one when several hosts compose the same modules, such as a web host and a
    ///     worker host, so the time-based cutter and the publish retrier run once. Explicit cuts, expiry and
    ///     verification are unaffected. The composition model's <c>auditing</c> section shows the setting.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     The default delay between passes that succeed.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     The default delay after the first failed pass, doubled after each further failure.
    /// </summary>
    public static readonly TimeSpan DefaultInitialRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     The default longest delay between failed passes.
    /// </summary>
    public static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Gets or sets the delay between passes that succeed. Must be positive.
    /// </summary>
    /// <value>
    ///     The default is <see cref="DefaultPollInterval" />.
    /// </value>
    public TimeSpan PollInterval { get; set; } = DefaultPollInterval;

    /// <summary>
    ///     Gets or sets the delay after the first failed pass, doubled after each further failure. Must be positive.
    /// </summary>
    /// <value>
    ///     The default is <see cref="DefaultInitialRetryDelay" />.
    /// </value>
    public TimeSpan InitialRetryDelay { get; set; } = DefaultInitialRetryDelay;

    /// <summary>
    ///     Gets or sets the longest delay between failed passes. Must be positive.
    /// </summary>
    /// <value>
    ///     The default is <see cref="DefaultMaxRetryDelay" />.
    /// </value>
    public TimeSpan MaxRetryDelay { get; set; } = DefaultMaxRetryDelay;

    /// <summary>
    ///     Validates the configured values before the service starts.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="PollInterval" />, <see cref="InitialRetryDelay" />, or <see cref="MaxRetryDelay" /> is zero or
    ///     negative.
    /// </exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(PollInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(InitialRetryDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxRetryDelay, TimeSpan.Zero);
    }
}
