using System;
using System.Globalization;
using LiteBus.Inbox.Abstractions;
using LiteBus.Transport;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Ingress.Transport;

/// <summary>
///     Maps transport deliveries to inbox acceptance metadata.
/// </summary>
internal static class TransportInboxIngressMapper
{
    /// <summary>
    ///     Builds inbox metadata from LiteBus transport headers and message properties.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <returns>The inbox options passed to <see cref="IInbox.AcceptAsync" />.</returns>
    public static InboxOptions ToInboxOptions(TransportMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new InboxOptions
        {
            Id = TryGetMessageId(message),
            IdempotencyKey = TransportHeaderValues.GetString(message.Headers, TransportHeaders.IdempotencyKey),
            CorrelationId = TransportHeaderValues.GetString(message.Headers, TransportHeaders.CorrelationId) ?? message.CorrelationId,
            CausationId = TransportHeaderValues.GetString(message.Headers, TransportHeaders.CausationId),
            TenantId = TransportHeaderValues.GetString(message.Headers, TransportHeaders.TenantId),
            TraceContext = TransportHeaderValues.GetString(message.Headers, TransportHeaders.TraceContext),
            VisibleAfter = TryGetVisibleAfter(message)
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
            throw new TransportHeaderMappingException($"Transport header '{headerName}' is required.");
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
            throw new TransportHeaderMappingException(
                $"Transport header '{TransportHeaders.ContractVersion}' must contain a positive integer.");
        }

        return version;
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
