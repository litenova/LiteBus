using System;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Dispatch;

/// <summary>
///     Configures how <see cref="TransportOutboxDispatcher" /> publishes leased outbox envelopes through a transport.
/// </summary>
public sealed class TransportOutboxDispatcherOptions
{
    /// <summary>
    ///     Gets or sets the default destination address used when publishing leased outbox envelopes.
    /// </summary>
    /// <value>
    ///     For AMQP this is the exchange name. Use an empty string for the default direct exchange that routes by
    ///     queue name.
    /// </value>
    public string DefaultDestination { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the MIME content type written to transport message properties.
    /// </summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    ///     Gets or sets a value indicating whether published messages should be persisted by the broker.
    /// </summary>
    public bool Persistent { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether unroutable messages must cause the publish operation to fail.
    /// </summary>
    public bool Mandatory { get; set; }

    /// <summary>
    ///     Gets or sets the optional route resolver invoked for each envelope.
    /// </summary>
    /// <value>
    ///     When unset, the dispatcher uses <see cref="OutboxEnvelope.Topic" /> when present, otherwise
    ///     <see cref="OutboxEnvelope.ContractName" />.
    /// </value>
    public Func<OutboxEnvelope, string>? ResolveRoute { get; set; }
}
