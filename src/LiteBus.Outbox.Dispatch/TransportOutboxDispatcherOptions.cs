using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Abstractions.Exceptions;
using LiteBus.Transport.Abstractions;

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
    /// <remarks>
    ///     Honored by the AMQP transport. Ignored by the SQS, Kafka, and Azure Service Bus transports, which expose no
    ///     per-message persistence flag on publish.
    /// </remarks>
    public bool Persistent { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether unroutable messages must cause the publish operation to fail.
    /// </summary>
    /// <remarks>
    ///     AMQP-only semantics: only the AMQP transport fails the publish operation for unroutable messages. The SQS,
    ///     Kafka, and Azure Service Bus transports ignore this flag.
    /// </remarks>
    public bool Mandatory { get; set; } = true;

    /// <summary>
    ///     Gets or sets the optional tenant routing strategy the dispatcher consults for each envelope.
    /// </summary>
    /// <value>
    ///     When set, the dispatch module registers it as the <see cref="ITenantRoutingStrategy" /> the dispatcher
    ///     resolves, so a multi-tenant host routes each tenant partition to its own destination.
    /// </value>
    public ITenantRoutingStrategy? TenantRoutingStrategy { get; set; }

    /// <summary>
    ///     Gets or sets the optional route resolver invoked for each envelope.
    /// </summary>
    /// <value>
    ///     When set, the delegate wins over <see cref="OutboxEnvelope.Topic" /> and its result must be non-empty; an
    ///     empty result fails the dispatch with <see cref="LiteBusDispatchException" />. When unset, the dispatcher uses
    ///     <see cref="OutboxEnvelope.Topic" /> when present, otherwise <see cref="OutboxEnvelope.ContractName" />.
    /// </value>
    public Func<OutboxEnvelope, string>? ResolveRoute { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether leased payloads are deserialized to validate contract wiring before publish.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> by default. When <see langword="true" />, dispatch validates payloads by deserializing
    ///     them before publishing the stored bytes to the transport.
    /// </value>
    public bool ValidatePayloadBeforeDispatch { get; set; }
}
