using System;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Connection settings for AMQP brokers such as RabbitMQ and LavinMQ.
/// </summary>
public sealed class AmqpConnectionOptions
{
    /// <summary>
    ///     Gets the optional AMQP URI used instead of discrete host, port, and credential fields.
    /// </summary>
    /// <value>
    ///     When set, the URI overrides <see cref="HostName" />, <see cref="Port" />, <see cref="UserName" />,
    ///     <see cref="Password" />, and <see cref="VirtualHost" />.
    /// </value>
    public Uri? Uri { get; init; }

    /// <summary>
    ///     Gets the broker host name.
    /// </summary>
    public string HostName { get; init; } = "localhost";

    /// <summary>
    ///     Gets the broker port.
    /// </summary>
    public int Port { get; init; } = 5672;

    /// <summary>
    ///     Gets the AMQP virtual host.
    /// </summary>
    public string VirtualHost { get; init; } = "/";

    /// <summary>
    ///     Gets the username used to authenticate with the broker.
    /// </summary>
    public string UserName { get; init; } = "guest";

    /// <summary>
    ///     Gets the password used to authenticate with the broker.
    /// </summary>
    public string Password { get; init; } = "guest";

    /// <summary>
    ///     Gets the optional client-provided connection name shown in broker management tools.
    /// </summary>
    public string? ClientProvidedName { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the client should automatically recover dropped connections.
    /// </summary>
    public bool AutomaticRecoveryEnabled { get; init; } = true;

    /// <summary>
    ///     Gets the interval between network recovery attempts.
    /// </summary>
    public TimeSpan NetworkRecoveryInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Gets the circuit breaker settings applied to connection and publish operations.
    /// </summary>
    public AmqpCircuitBreakerOptions CircuitBreaker { get; init; } = new();
}
