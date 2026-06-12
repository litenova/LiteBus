using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Default implementation of <see cref="IInboxEnvelopeFactory" />.
/// </summary>
public sealed class InboxEnvelopeFactory : IInboxEnvelopeFactory
{
    /// <summary>
    ///     Gets the time provider used to stamp acceptance time and resolve relative visibility.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the registry used to map the runtime message type to a stable contract.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the optional resolver that selects the CLR type used for contract lookup.
    /// </summary>
    private readonly IMessageContractResolver? _contractTypeResolver;

    /// <summary>
    ///     Gets the serializer used to create the serialized payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the optional inbox protector applied before payloads are written to storage.
    /// </summary>
    private readonly IInboxPayloadProtector? _payloadProtector;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxEnvelopeFactory" /> class.
    /// </summary>
    /// <param name="contractRegistry">The registry used to map message types to stable contracts.</param>
    /// <param name="messageSerializer">The serializer used to create the serialized payload.</param>
    /// <param name="clock">The time provider used to stamp acceptance time.</param>
    /// <param name="payloadProtector">The optional inbox protector applied before payloads are written to storage.</param>
    /// <param name="contractTypeResolver">
    ///     The optional resolver that overrides contract lookup type selection. When omitted, the runtime message type is
    ///     used.
    /// </param>
    public InboxEnvelopeFactory(
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        TimeProvider clock,
        IInboxPayloadProtector? payloadProtector = null,
        IMessageContractResolver? contractTypeResolver = null)
    {
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _payloadProtector = payloadProtector;
        _contractTypeResolver = contractTypeResolver;
    }

    /// <inheritdoc />
    public Task<InboxEnvelope> CreateAsync(
        InboxAcceptItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var messageType = item.MessageType ?? item.Message.GetType();
        return CreateCoreAsync(item.Message, messageType, item.Metadata, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxEnvelope>> CreateBatchAsync(
        IReadOnlyList<InboxAcceptItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return Array.Empty<InboxEnvelope>();
        }

        var tasks = new Task<InboxEnvelope>[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];

            tasks[index] = CreateCoreAsync(
                item.Message,
                item.MessageType ?? item.Message.GetType(),
                item.Metadata,
                cancellationToken);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates one inbox envelope from a message instance and acceptance metadata.
    /// </summary>
    /// <param name="message">The message instance to serialize.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <param name="metadata">The acceptance metadata applied outside the payload.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>The inbox envelope ready for store persistence or staging.</returns>
    private async Task<InboxEnvelope> CreateCoreAsync(
        object message,
        Type messageType,
        InboxAcceptMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(metadata);

        if (!messageType.IsInstanceOfType(message))
        {
            throw new ArgumentException(
                $"The supplied message instance is not assignable to '{messageType.FullName}'.",
                nameof(message));
        }

        var contract = _contractRegistry.GetContract(ResolveContractType(messageType, message));
        var acceptedAt = _clock.GetUtcNow();
        var id = DurableEnvelopeMetadataMapper.ResolveMessageId(metadata.Identity);
        var payload = await _messageSerializer.SerializeAsync(message, cancellationToken).ConfigureAwait(false);
        payload = await PayloadProtection.ProtectAsync(payload, _payloadProtector, cancellationToken).ConfigureAwait(false);
        var (correlationId, causationId, traceContext) = DurableEnvelopeMetadataMapper.ResolveTraceColumns(metadata.Trace);

        return new InboxEnvelope
        {
            Id = id,
            ContractName = contract.Name,
            ContractVersion = contract.Version,
            Payload = payload,
            CreatedAt = acceptedAt,
            VisibleAfter = DurableEnvelopeMetadataMapper.ResolveVisibleAfter(metadata.Visibility, _clock),
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = DurableEnvelopeMetadataMapper.ResolveIdempotencyKey(metadata.Idempotency),
            IdempotencyConflictMode = DurableEnvelopeMetadataMapper.ResolveIdempotencyConflictMode(metadata.Idempotency),
            CorrelationId = correlationId,
            CausationId = causationId,
            TenantId = DurableEnvelopeMetadataMapper.ResolveTenantId(metadata.Tenant),
            TraceContext = traceContext
        };
    }

    /// <summary>
    ///     Resolves the CLR type passed to <see cref="IContractReader.GetContract" /> for one acceptance call.
    /// </summary>
    /// <param name="declaredType">The declared message type supplied by the caller.</param>
    /// <param name="message">The message instance being accepted.</param>
    /// <returns>The CLR type used for contract lookup.</returns>
    private Type ResolveContractType(Type declaredType, object message)
    {
        return _contractTypeResolver?.ResolveContractType(declaredType, message) ?? message.GetType();
    }
}