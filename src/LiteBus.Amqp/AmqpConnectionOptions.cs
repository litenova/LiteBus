using System;

namespace LiteBus.Amqp;

/// <summary>
///     Connection settings for AMQP brokers such as RabbitMQ and LavinMQ.
/// </summary>
public sealed class AmqpConnectionOptions
{
    /// <summary>
    ///     Gets or sets the optional AMQP URI used instead of discrete host, port, and credential fields.
    /// </summary>
    /// <value>
    ///     When set, the URI overrides <see cref="HostName" />, <see cref="Port" />, <see cref="UserName" />,
    ///     <see cref="Password" />, and <see cref="VirtualHost" />.
    /// </value>
    public Uri? Uri { get; set; }

    /// <summary>
    ///     Gets or sets the broker host name.
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    ///     Gets or sets the broker port.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    ///     Gets or sets the AMQP virtual host.
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    ///     Gets or sets the username used to authenticate with the broker.
    /// </summary>
    public string UserName { get; set; } = "guest";

    /// <summary>
    ///     Gets or sets the password used to authenticate with the broker.
    /// </summary>
    public string Password { get; set; } = "guest";

    /// <summary>
    ///     Gets or sets the optional client-provided connection name shown in broker management tools.
    /// </summary>
    public string? ClientProvidedName { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the client should automatically recover dropped connections.
    /// </summary>
    public bool AutomaticRecoveryEnabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the interval between network recovery attempts.
    /// </summary>
    public TimeSpan NetworkRecoveryInterval { get; set; } = TimeSpan.FromSeconds(5);
}
