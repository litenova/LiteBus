using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Maps transport deliveries to inbox acceptance metadata.
/// </summary>
internal static class TransportInboxIngressMapper
{
    /// <summary>
    ///     Builds inbox acceptance metadata from LiteBus transport headers and message properties.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="mappingOptions">The ingress mapping policy applied to identity and idempotency headers.</param>
    /// <returns>The metadata passed to <see cref="IInbox.AcceptAsync(InboxAcceptItem, System.Threading.CancellationToken)" />.</returns>
    public static InboxAcceptMetadata ToInboxAcceptMetadata(
        TransportMessage message,
        TransportInboxIngressMappingOptions mappingOptions)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(mappingOptions);

        return new InboxAcceptMetadata
        {
            Identity = ResolveIdentity(message, mappingOptions),
            Idempotency = ResolveIdempotency(message, mappingOptions),
            Visibility = ResolveVisibility(message),
            Trace = ResolveTrace(message),
            Tenant = ResolveTenant(message, mappingOptions)
        };
    }

    /// <summary>
    ///     Reads a required string header from the delivery.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="headerName">The header name to read.</param>
    /// <returns>The header value.</returns>
    /// <exception cref="TransportHeaderMappingException">The header is missing or empty.</exception>
    public static string GetRequiredHeader(TransportMessage message, string headerName)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);

        var value = TransportHeaderValues.GetString(message.Headers, headerName);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw TransportHeaderMappingException.MissingRequiredHeader(headerName);
        }

        return value;
    }

    /// <summary>
    ///     Reads the contract version header and parses it as a positive integer.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>The contract version.</returns>
    /// <exception cref="TransportHeaderMappingException">The header is missing or not a positive integer.</exception>
    public static int GetRequiredContractVersion(TransportMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var rawValue = GetRequiredHeader(message, TransportHeaders.ContractVersion);

        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version <= 0)
        {
            throw TransportHeaderMappingException.InvalidRequiredHeader(
                TransportHeaders.ContractVersion,
                "the value must be a positive integer.");
        }

        return version;
    }

    /// <summary>
    ///     Resolves message identity metadata from the broker delivery id and optional LiteBus headers.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="mappingOptions">The ingress mapping policy applied to identity resolution.</param>
    /// <returns>Supplied identity derived from the broker delivery id or LiteBus headers.</returns>
    /// <exception cref="InboxIngressException">A stable broker delivery id is required but missing.</exception>
    private static MessageIdentity ResolveIdentity(
        TransportMessage message,
        TransportInboxIngressMappingOptions mappingOptions)
    {
        if (mappingOptions.TrustApplicationHeaders)
        {
            var headerMessageId = TryGetMessageId(message);

            if (headerMessageId is { } suppliedId)
            {
                return new MessageIdentity.Supplied(suppliedId);
            }
        }

        var brokerDeliveryId = TryGetBrokerDeliveryId(message);

        if (string.IsNullOrWhiteSpace(brokerDeliveryId))
        {
            if (mappingOptions.RequireStableIdentity)
            {
                throw new InboxIngressException(
                    "Ingress requires a stable broker delivery id. Supply a transport message id or disable RequireStableIdentity.");
            }

            return MessageIdentity.Generated.Instance;
        }

        return new MessageIdentity.Supplied(CreateIdentityFromBrokerDeliveryId(brokerDeliveryId));
    }

    /// <summary>
    ///     Resolves idempotency metadata from the broker delivery id and optional LiteBus headers.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="mappingOptions">The ingress mapping policy applied to idempotency resolution.</param>
    /// <returns>Keyed idempotency scoped to the broker delivery when a stable id is present.</returns>
    /// <exception cref="InboxIngressException">A stable broker delivery id is required but missing.</exception>
    private static Idempotency ResolveIdempotency(
        TransportMessage message,
        TransportInboxIngressMappingOptions mappingOptions)
    {
        if (mappingOptions.TrustApplicationHeaders)
        {
            var key = TransportHeaderValues.GetString(message.Headers, TransportHeaders.IdempotencyKey);

            if (!string.IsNullOrWhiteSpace(key))
            {
                return new Idempotency.Keyed(key);
            }
        }

        var brokerDeliveryId = TryGetBrokerDeliveryId(message);

        if (string.IsNullOrWhiteSpace(brokerDeliveryId))
        {
            if (mappingOptions.RequireStableIdentity)
            {
                throw new InboxIngressException(
                    "Ingress requires a stable broker delivery id for idempotency. Supply a transport message id or disable RequireStableIdentity.");
            }

            return Idempotency.None.Instance;
        }

        return new Idempotency.Keyed(CreateBrokerScopedIdempotencyKey(message, brokerDeliveryId));
    }

    /// <summary>
    ///     Resolves visibility metadata from the optional visible-after header.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>Deferred visibility when a valid timestamp is present; otherwise immediate visibility.</returns>
    private static MessageVisibility ResolveVisibility(TransportMessage message)
    {
        var delay = TryGetVisibleAfterDelay(message);

        if (delay is { } relativeDelay)
        {
            return new MessageVisibility.After(relativeDelay);
        }

        var visibleAfter = TryGetVisibleAfter(message);

        return visibleAfter is { } at
            ? new MessageVisibility.At(at)
            : MessageVisibility.Immediate.Instance;
    }

    /// <summary>
    ///     Resolves trace metadata from correlation, causation, and trace-context headers.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>The trace specification represented by the ingress headers.</returns>
    private static MessageTrace ResolveTrace(TransportMessage message)
    {
        var correlationId = TransportHeaderValues.GetString(message.Headers, TransportHeaders.CorrelationId) ?? message.CorrelationId;
        var causationId = TransportHeaderValues.GetString(message.Headers, TransportHeaders.CausationId);
        var traceContext = TransportHeaderValues.GetString(message.Headers, TransportHeaders.TraceContext);

        if (!string.IsNullOrWhiteSpace(traceContext) && !string.IsNullOrWhiteSpace(correlationId) && !string.IsNullOrWhiteSpace(causationId))
        {
            return new MessageTrace.Distributed(correlationId, causationId, traceContext);
        }

        if (!string.IsNullOrWhiteSpace(correlationId) && !string.IsNullOrWhiteSpace(causationId))
        {
            return new MessageTrace.Workflow(correlationId, causationId);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            return new MessageTrace.Correlated(correlationId);
        }

        return MessageTrace.None.Instance;
    }

    /// <summary>
    ///     Resolves tenant metadata from the optional tenant identifier header when application headers are trusted.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="mappingOptions">The ingress mapping policy applied to tenant resolution.</param>
    /// <returns>Isolated tenant scope when trusted headers supply a tenant id; otherwise unscoped.</returns>
    private static TenantScope ResolveTenant(
        TransportMessage message,
        TransportInboxIngressMappingOptions mappingOptions)
    {
        if (!mappingOptions.TrustApplicationHeaders)
        {
            return TenantScope.Unscoped.Instance;
        }

        var tenantId = TransportHeaderValues.GetString(message.Headers, TransportHeaders.TenantId);

        return !string.IsNullOrWhiteSpace(tenantId)
            ? new TenantScope.Isolated(tenantId)
            : TenantScope.Unscoped.Instance;
    }

    /// <summary>
    ///     Parses the optional LiteBus message identifier header into a <see cref="Guid" />.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>The message identifier, or <see langword="null" /> when the header is absent or invalid.</returns>
    private static Guid? TryGetMessageId(TransportMessage message)
    {
        var rawValue = TransportHeaderValues.GetString(message.Headers, TransportHeaders.MessageId) ?? message.MessageId;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return Guid.TryParse(rawValue, out var messageId) ? messageId : null;
    }

    /// <summary>
    ///     Gets the broker delivery identifier used for ingress identity and idempotency.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>The broker delivery id, or <see langword="null" /> when none is available.</returns>
    private static string? TryGetBrokerDeliveryId(TransportMessage message)
    {
        var rawValue = TransportHeaderValues.GetString(message.Headers, TransportHeaders.MessageId) ?? message.MessageId;

        return string.IsNullOrWhiteSpace(rawValue) ? null : rawValue;
    }

    /// <summary>
    ///     Creates a stable inbox message id from a broker delivery identifier.
    /// </summary>
    /// <param name="brokerDeliveryId">The broker-scoped delivery identifier.</param>
    /// <returns>A supplied identity GUID derived from the broker delivery id.</returns>
    private static Guid CreateIdentityFromBrokerDeliveryId(string brokerDeliveryId)
    {
        if (Guid.TryParse(brokerDeliveryId, out var messageId))
        {
            return messageId;
        }

        return CreateDeterministicGuid($"ingress-identity:{brokerDeliveryId}");
    }

    /// <summary>
    ///     Creates a broker-scoped idempotency key for ingress deduplication on redelivery.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="brokerDeliveryId">The broker-scoped delivery identifier.</param>
    /// <returns>The idempotency key stored with the accepted envelope.</returns>
    private static string CreateBrokerScopedIdempotencyKey(TransportMessage message, string brokerDeliveryId)
    {
        var destination = string.IsNullOrWhiteSpace(message.Destination) ? "unknown" : message.Destination;
        return $"ingress:{destination}:{brokerDeliveryId}";
    }

    /// <summary>
    ///     Creates a deterministic GUID from a stable string value.
    /// </summary>
    /// <param name="value">The input string hashed into a GUID.</param>
    /// <returns>A name-based GUID suitable for supplied identity metadata.</returns>
    private static Guid CreateDeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    /// <summary>
    ///     Parses the optional visible-after header into a UTC timestamp.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>The visible-after timestamp, or <see langword="null" /> when the header is absent or invalid.</returns>
    private static DateTimeOffset? TryGetVisibleAfter(TransportMessage message)
    {
        var rawValue = TransportHeaderValues.GetString(message.Headers, TransportHeaders.VisibleAfter);

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var visibleAfter)
            ? visibleAfter
            : null;
    }

    /// <summary>
    ///     Parses the optional relative visibility delay header into a <see cref="TimeSpan" />.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>The relative delay, or <see langword="null" /> when the header is absent or invalid.</returns>
    private static TimeSpan? TryGetVisibleAfterDelay(TransportMessage message)
    {
        var rawValue = TransportHeaderValues.GetString(message.Headers, TransportHeaders.VisibleAfterDelay);

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (TimeSpan.TryParse(rawValue, CultureInfo.InvariantCulture, out var delay) && delay >= TimeSpan.Zero)
        {
            return delay;
        }

        if (long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) && ticks >= 0)
        {
            return TimeSpan.FromTicks(ticks);
        }

        return null;
    }
}
