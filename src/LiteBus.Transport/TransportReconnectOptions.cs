using System;

namespace LiteBus.Transport;

/// <summary>
///     Configures automatic reconnect behavior shared by transport adapters.
/// </summary>
public sealed class TransportReconnectOptions
{
    /// <summary>
    ///     Gets a value indicating whether the client should automatically recover dropped connections.
    /// </summary>
    public bool AutomaticRecoveryEnabled { get; init; } = true;

    /// <summary>
    ///     Gets the interval between network recovery attempts.
    /// </summary>
    public TimeSpan RecoveryInterval { get; init; } = TimeSpan.FromSeconds(5);
}
