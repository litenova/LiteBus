using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Reports the effective outbox reaction retry composition: whether it runs, which policy decides, and the
///     bound and delay curve in force.
/// </summary>
/// <remarks>
///     The section snapshots the outbox builder while the module builds. The policy entry names the default when
///     the application registered nothing and the replacement when it did, so the composition export shows which
///     ports were replaced. The value <see cref="GetData" /> returns holds ordinal string keys and primitives, so
///     the composition export stays deterministic.
/// </remarks>
public sealed class OutboxReactionRetryCompositionSection : ICompositionSectionContributor
{
    /// <summary>
    ///     Whether a conflicted reaction publishes again inside the same dispatch. Snapshot at construction.
    /// </summary>
    private readonly bool _enabled;

    /// <summary>
    ///     The policy type name that decides retries. Snapshot at construction.
    /// </summary>
    private readonly string _policy;

    /// <summary>
    ///     Whether the policy is the framework default. Snapshot at construction.
    /// </summary>
    private readonly bool _isDefault;

    /// <summary>
    ///     The total in-dispatch attempts per delivery. Snapshot at construction.
    /// </summary>
    private readonly int _maxAttempts;

    /// <summary>
    ///     The delay before the first in-dispatch retry, in milliseconds. Snapshot at construction.
    /// </summary>
    private readonly double _initialDelayMs;

    /// <summary>
    ///     The upper bound on a calculated in-dispatch retry delay, in milliseconds. Snapshot at construction.
    /// </summary>
    private readonly double _maxDelayMs;

    /// <summary>
    ///     Whether in-dispatch retry delays include jitter. Snapshot at construction.
    /// </summary>
    private readonly bool _useJitter;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxReactionRetryCompositionSection" /> class.
    /// </summary>
    /// <param name="options">The reaction retry options collected from the outbox builder.</param>
    /// <param name="policy">
    ///     The configured policy as an implementation type or a pre-created instance, or
    ///     <see langword="null" /> when the application registered none.
    /// </param>
    internal OutboxReactionRetryCompositionSection(OutboxReactionRetryOptions options, object? policy)
    {
        ArgumentNullException.ThrowIfNull(options);

        _enabled = options.Enabled;
        _policy = DescribePolicy(policy);
        _isDefault = policy is null;
        _maxAttempts = options.MaxAttempts;
        _initialDelayMs = options.InitialDelay.TotalMilliseconds;
        _maxDelayMs = options.MaxDelay.TotalMilliseconds;
        _useJitter = options.UseJitter;
    }

    /// <summary>
    ///     Gets a value indicating whether a conflicted reaction publishes again inside the same dispatch.
    /// </summary>
    public bool Enabled => _enabled;

    /// <summary>
    ///     Gets the policy type name that decides retries.
    /// </summary>
    public string Policy => _policy;

    /// <summary>
    ///     Gets a value indicating whether the policy is the framework default.
    /// </summary>
    public bool IsDefault => _isDefault;

    /// <inheritdoc />
    public string SectionName => "outbox-reaction-retry";

    /// <inheritdoc />
    public object? GetData()
    {
        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["enabled"] = _enabled,
            ["initialDelayMs"] = _initialDelayMs,
            ["isDefault"] = _isDefault,
            ["maxAttempts"] = _maxAttempts,
            ["maxDelayMs"] = _maxDelayMs,
            ["policy"] = _policy,
            ["useJitter"] = _useJitter,
        };
    }

    /// <summary>
    ///     Names the policy the composition resolves.
    /// </summary>
    /// <param name="policy">
    ///     The configured policy as an implementation type or a pre-created instance, or
    ///     <see langword="null" /> when the application registered none.
    /// </param>
    /// <returns>The default policy type name, or the configured type or instance type name.</returns>
    private static string DescribePolicy(object? policy)
    {
        var type = policy switch
        {
            null => typeof(DefaultOutboxReactionRetryPolicy),
            Type implementationType => implementationType,
            _ => policy.GetType(),
        };

        return type.FullName ?? type.Name;
    }
}
