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
    ///     Gets the time provider used to stamp acceptance time.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the registry used to map the runtime message type to a stable contract.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the serializer used to create the serialized payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the optional inbox protector applied before payloads are written to storage.
    /// </summary>
    private readonly IInboxPayloadProtector? _payloadProtector;

    /// <summary>
    ///     Gets the optional resolver that selects the CLR type used for contract lookup.
    /// </summary>
    private readonly IMessageContractResolver? _contractTypeResolver;

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
    public async Task<InboxEnvelope> CreateAsync(
        object message,
        Type messageType,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageType);

        if (!messageType.IsInstanceOfType(message))
        {
            throw new ArgumentException(
                $"The supplied message instance is not assignable to '{messageType.FullName}'.",
                nameof(message));
        }

        options ??= new InboxOptions();

        var contract = _contractRegistry.GetContract(ResolveContractType(messageType, message));
        var acceptedAt = _clock.GetUtcNow();
        var id = options.Id ?? Guid.NewGuid();
        var payload = await _messageSerializer.SerializeAsync(message, cancellationToken).ConfigureAwait(false);
        payload = await PayloadProtection.ProtectAsync(payload, _payloadProtector, cancellationToken).ConfigureAwait(false);

        return new InboxEnvelope
        {
            Id = id,
            ContractName = contract.Name,
            ContractVersion = contract.Version,
            Payload = payload,
            CreatedAt = acceptedAt,
            VisibleAfter = options.VisibleAfter,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = string.IsNullOrWhiteSpace(options.IdempotencyKey) ? null : options.IdempotencyKey,
            CorrelationId = options.CorrelationId,
            CausationId = options.CausationId,
            TenantId = options.TenantId,
            TraceContext = options.TraceContext
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxEnvelope>> CreateBatchAsync(
        IReadOnlyList<object> messages,
        IReadOnlyList<Type> messageTypes,
        IReadOnlyList<InboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(messageTypes);

        if (messages.Count != messageTypes.Count)
        {
            throw new ArgumentException("Messages and message types must contain the same number of entries.");
        }

        if (options is not null && options.Count != messages.Count)
        {
            throw new ArgumentException("Options must contain the same number of entries as messages when supplied.");
        }

        if (messages.Count == 0)
        {
            return Array.Empty<InboxEnvelope>();
        }

        var envelopes = new InboxEnvelope[messages.Count];

        for (var index = 0; index < messages.Count; index++)
        {
            envelopes[index] = await CreateAsync(
                messages[index],
                messageTypes[index],
                options?[index],
                cancellationToken).ConfigureAwait(false);
        }

        return envelopes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxEnvelope>> CreateBatchAsync<T>(
        IReadOnlyList<T> messages,
        IReadOnlyList<InboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (options is not null && options.Count != messages.Count)
        {
            throw new ArgumentException("Options must contain the same number of entries as messages when supplied.");
        }

        if (messages.Count == 0)
        {
            return Array.Empty<InboxEnvelope>();
        }

        var envelopes = new InboxEnvelope[messages.Count];

        for (var index = 0; index < messages.Count; index++)
        {
            envelopes[index] = await CreateAsync(
                messages[index],
                typeof(T),
                options?[index],
                cancellationToken).ConfigureAwait(false);
        }

        return envelopes;
    }

    /// <summary>
    ///     Resolves the CLR type passed to <see cref="IContractReader.GetContract" /> for one acceptance call.
    /// </summary>
    /// <param name="declaredType">The declared message type supplied by the caller.</param>
    /// <param name="message">The message instance being accepted.</param>
    /// <returns>The CLR type used for contract lookup.</returns>
    private Type ResolveContractType(Type declaredType, object message) =>
        _contractTypeResolver?.ResolveContractType(declaredType, message) ?? message.GetType();
}
