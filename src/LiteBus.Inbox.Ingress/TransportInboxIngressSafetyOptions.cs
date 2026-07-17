using System;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Defines transport-independent safety limits applied before inbox acceptance.
/// </summary>
/// <remarks>
///     Broker adapters expose this value object so payload size, identity trust, and batch admission do not silently
///     vary by provider. Broker-specific options remain on their adapter types.
/// </remarks>
public sealed record TransportInboxIngressSafetyOptions
{
    /// <summary>
    ///     Gets the default maximum body size accepted by ingress.
    /// </summary>
    public const int DefaultMaxMessageBytes = 4 * 1024 * 1024;

    /// <summary>
    ///     Gets the maximum message body size accepted before deserialization.
    /// </summary>
    public int MaxMessageBytes { get; init; } = DefaultMaxMessageBytes;

    /// <summary>
    ///     Gets a value indicating whether a stable broker delivery identity is required.
    /// </summary>
    public bool RequireStableIdentity { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether trusted application headers may override broker identity and tenant metadata.
    /// </summary>
    public bool TrustApplicationHeaders { get; init; }

    /// <summary>
    ///     Gets a value indicating whether deliveries are accepted in batches.
    /// </summary>
    public bool EnableBatchAccept { get; init; }

    /// <summary>
    ///     Gets the maximum wait before a partial batch is accepted.
    /// </summary>
    public TimeSpan BatchMaxWait { get; init; } = TimeSpan.FromMilliseconds(200);
}
