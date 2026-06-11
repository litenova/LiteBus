using System;
using System.Globalization;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Transport;
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
    /// <returns>The metadata passed to <see cref="IInbox.AcceptBatchAsync" />.</returns>
    public static InboxAcceptMetadata ToInboxAcceptMetadata(TransportMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new InboxAcceptMetadata
        {
            Identity = ResolveIdentity(message),
            Idempotency = ResolveIdempotency(message),
            Visibility = ResolveVisibility(message),
            Trace = ResolveTrace(message),
            Tenant = ResolveTenant(message)
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
    ///     Resolves message identity metadata from transport headers and message properties.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>Supplied identity when a valid message id is present; otherwise generated identity.</returns>
    private static MessageIdentity ResolveIdentity(TransportMessage message)
    {
        var messageId = TryGetMessageId(message);

        return messageId is { } id
            ? new MessageIdentity.Supplied(id)
            : MessageIdentity.Generated.Instance;
    }

    /// <summary>
    ///     Resolves idempotency metadata from the optional idempotency key header.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>Keyed idempotency when a non-empty key is present; otherwise none.</returns>
    private static Idempotency ResolveIdempotency(TransportMessage message)
    {
        var key = TransportHeaderValues.GetString(message.Headers, TransportHeaders.IdempotencyKey);

        return !string.IsNullOrWhiteSpace(key)
            ? new Idempotency.Keyed(key)
            : Idempotency.None.Instance;
    }

    /// <summary>
    ///     Resolves visibility metadata from the optional visible-after header.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>Deferred visibility when a valid timestamp is present; otherwise immediate visibility.</returns>
    private static MessageVisibility ResolveVisibility(TransportMessage message)
    {
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
    ///     Resolves tenant metadata from the optional tenant identifier header.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>Isolated tenant scope when a non-empty tenant id is present; otherwise unscoped.</returns>
    private static TenantScope ResolveTenant(TransportMessage message)
    {
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
}