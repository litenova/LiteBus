using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Amqp;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Maps AMQP deliveries into <see cref="IInbox.AddAsync" /> acceptance calls.
/// </summary>
public sealed class AmqpInboxIngressHandler
{
    /// <summary>
    ///     Gets the AMQP header name for an optional idempotency key.
    /// </summary>
    private const string IdempotencyKeyHeader = "litebus-idempotency-key";

    /// <summary>
    ///     Gets the AMQP header name for an optional visible-after timestamp.
    /// </summary>
    private const string VisibleAfterHeader = "litebus-visible-after";

    /// <summary>
    ///     Gets the cached open generic <see cref="IInbox.AddAsync" /> method definition.
    /// </summary>
    private static readonly MethodInfo AddAsyncMethodDefinition =
        typeof(IInbox).GetMethod(nameof(IInbox.AddAsync), BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Could not resolve {nameof(IInbox.AddAsync)}.");

    /// <summary>
    ///     Gets the registry used to resolve persisted contracts back to CLR types.
    /// </summary>
    private readonly IMessageContractRegistry _contractRegistry;

    /// <summary>
    ///     Gets the inbox writer used to accept deserialized messages.
    /// </summary>
    private readonly IInbox _inbox;

    /// <summary>
    ///     Gets the serializer used to hydrate AMQP message bodies.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpInboxIngressHandler" /> class.
    /// </summary>
    /// <param name="inbox">The inbox writer used to accept deserialized messages.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to CLR types.</param>
    /// <param name="messageSerializer">The serializer used to hydrate AMQP message bodies.</param>
    public AmqpInboxIngressHandler(
        IInbox inbox,
        IMessageContractRegistry contractRegistry,
        IMessageSerializer messageSerializer)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
    }

    /// <summary>
    ///     Accepts one AMQP delivery into the inbox store.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <param name="cancellationToken">The token used to cancel deserialization or the store write.</param>
    /// <returns>A task that completes when the inbox accepts the message.</returns>
    public async Task AcceptAsync(AmqpReceivedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var contractName = GetRequiredHeader(message, AmqpHeaders.ContractName);
        var contractVersion = GetRequiredContractVersion(message);
        var messageType = _contractRegistry.GetMessageType(contractName, contractVersion);
        var payload = Encoding.UTF8.GetString(message.Body.Span);
        var deserialized = await _messageSerializer
            .DeserializeAsync(messageType, payload, cancellationToken)
            .ConfigureAwait(false);

        var options = BuildInboxOptions(message);
        await InvokeAddAsync(deserialized, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Invokes <see cref="IInbox.AddAsync" /> for a runtime message type.
    /// </summary>
    /// <param name="message">The deserialized message instance.</param>
    /// <param name="options">The inbox metadata mapped from AMQP headers.</param>
    /// <param name="cancellationToken">The token used to cancel the store write.</param>
    /// <returns>A task that completes when the inbox accepts the message.</returns>
    private async Task InvokeAddAsync(object message, InboxOptions options, CancellationToken cancellationToken)
    {
        var addAsync = AddAsyncMethodDefinition.MakeGenericMethod(message.GetType());
        var task = (Task)addAsync.Invoke(_inbox, [message, options, cancellationToken])!;
        await task.ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds inbox metadata from LiteBus AMQP headers and message properties.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <returns>The inbox options passed to <see cref="IInbox.AddAsync" />.</returns>
    private static InboxOptions BuildInboxOptions(AmqpReceivedMessage message)
    {
        var options = new InboxOptions
        {
            Id = TryGetMessageId(message),
            IdempotencyKey = GetOptionalHeader(message, IdempotencyKeyHeader),
            CorrelationId = GetOptionalHeader(message, AmqpHeaders.CorrelationId) ?? message.CorrelationId,
            CausationId = GetOptionalHeader(message, AmqpHeaders.CausationId),
            TenantId = GetOptionalHeader(message, AmqpHeaders.TenantId),
            VisibleAfter = TryGetVisibleAfter(message)
        };

        return options;
    }

    /// <summary>
    ///     Reads a required string header from the delivery.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <param name="headerName">The header name to read.</param>
    /// <returns>The header value.</returns>
    private static string GetRequiredHeader(AmqpReceivedMessage message, string headerName)
    {
        var value = GetOptionalHeader(message, headerName);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"AMQP header '{headerName}' is required for inbox ingress.");
        }

        return value;
    }

    /// <summary>
    ///     Reads the contract version header and parses it as a positive integer.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <returns>The contract version.</returns>
    private static int GetRequiredContractVersion(AmqpReceivedMessage message)
    {
        var rawValue = GetRequiredHeader(message, AmqpHeaders.ContractVersion);

        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version <= 0)
        {
            throw new InvalidOperationException(
                $"AMQP header '{AmqpHeaders.ContractVersion}' must contain a positive integer.");
        }

        return version;
    }

    /// <summary>
    ///     Reads an optional string header from the delivery.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <param name="headerName">The header name to read.</param>
    /// <returns>The header value, or <see langword="null" /> when the header is absent.</returns>
    private static string? GetOptionalHeader(AmqpReceivedMessage message, string headerName)
    {
        if (!message.Headers.TryGetValue(headerName, out var value) || value is null)
        {
            return null;
        }

        return ConvertHeaderValue(value);
    }

    /// <summary>
    ///     Converts an AMQP header value to a string when possible.
    /// </summary>
    /// <param name="value">The raw header value from the broker.</param>
    /// <returns>The string representation, or <see langword="null" /> when the value is absent.</returns>
    private static string? ConvertHeaderValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            Memory<byte> memory => Encoding.UTF8.GetString(memory.Span),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    ///     Parses the optional LiteBus message identifier header into a <see cref="Guid" />.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <returns>The message identifier, or <see langword="null" /> when the header is absent or invalid.</returns>
    private static Guid? TryGetMessageId(AmqpReceivedMessage message)
    {
        var rawValue = GetOptionalHeader(message, AmqpHeaders.MessageId) ?? message.MessageId;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return Guid.TryParse(rawValue, out var messageId) ? messageId : null;
    }

    /// <summary>
    ///     Parses the optional visible-after header into a UTC timestamp.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <returns>The visible-after timestamp, or <see langword="null" /> when the header is absent or invalid.</returns>
    private static DateTimeOffset? TryGetVisibleAfter(AmqpReceivedMessage message)
    {
        var rawValue = GetOptionalHeader(message, VisibleAfterHeader);

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var visibleAfter)
            ? visibleAfter
            : null;
    }
}
