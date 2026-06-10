using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Enqueues events through <see cref="LiteBusOutboxSaveChangesInterceptor" /> so contract resolution and
///     serialization follow the same path as <see cref="IOutbox" /> while rows commit with the active
///     <see cref="DbContext" /> transaction.
/// </summary>
/// <typeparam name="TContext">The application database context type bound to the current scope.</typeparam>
public sealed class TransactionalOutbox<TContext> : ITransactionalOutbox<TContext>
    where TContext : DbContext
{
    /// <summary>
    ///     Gets the time provider used to stamp storage time on staged envelopes.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the registry used to map the runtime event type to a stable contract.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the database context that owns the ambient transaction for staged envelopes.
    /// </summary>
    private readonly TContext _dbContext;

    /// <summary>
    ///     Gets the interceptor that queues envelopes until the next <c>SaveChanges</c> call.
    /// </summary>
    private readonly LiteBusOutboxSaveChangesInterceptor _interceptor;

    /// <summary>
    ///     Gets the serializer used to create the serialized payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the optional outbox protector applied before payloads are staged.
    /// </summary>
    private readonly IOutboxPayloadProtector? _payloadProtector;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransactionalOutbox{TContext}" /> class.
    /// </summary>
    /// <param name="interceptor">The interceptor that stages envelopes for the active context.</param>
    /// <param name="dbContext">The database context that owns the ambient transaction.</param>
    /// <param name="contractRegistry">The registry used to map the runtime event type to a stable contract.</param>
    /// <param name="messageSerializer">The serializer used to create the serialized payload.</param>
    /// <param name="clock">The time provider used to stamp storage time on staged envelopes.</param>
    /// <param name="payloadProtector">The optional outbox protector applied before payloads are staged.</param>
    public TransactionalOutbox(
        LiteBusOutboxSaveChangesInterceptor interceptor,
        TContext dbContext,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        TimeProvider clock,
        IOutboxPayloadProtector? payloadProtector = null)
    {
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _payloadProtector = payloadProtector;
    }

    /// <inheritdoc />
    public async Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        TEvent @event,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);

        options ??= new OutboxOptions();

        var eventType = @event.GetType();
        var contract = _contractRegistry.GetContract(eventType);
        var storedAt = _clock.GetUtcNow();
        var messageId = options.Id ?? Guid.NewGuid();
        var payload = await _messageSerializer.SerializeAsync(@event, cancellationToken).ConfigureAwait(false);
        payload = await ProtectPayloadAsync(payload, cancellationToken).ConfigureAwait(false);

        var envelope = new OutboxEnvelope
        {
            Id = messageId,
            ContractName = contract.Name,
            ContractVersion = contract.Version,
            Payload = payload,
            Topic = options.Topic,
            CreatedAt = storedAt,
            VisibleAfter = options.VisibleAfter,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            CorrelationId = options.CorrelationId,
            CausationId = options.CausationId,
            TenantId = options.TenantId,
            IdempotencyKey = string.IsNullOrWhiteSpace(options.IdempotencyKey) ? null : options.IdempotencyKey,
            TraceContext = options.TraceContext
        };

        _interceptor.Enqueue(_dbContext, envelope);

        return new OutboxReceipt<TEvent>
        {
            Id = envelope.Id,
            MessageType = eventType,
            ContractName = envelope.ContractName,
            ContractVersion = envelope.ContractVersion,
            StoredAt = envelope.CreatedAt,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            TenantId = envelope.TenantId
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<object> events,
        IReadOnlyList<Type> eventTypes,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(eventTypes);

        if (events.Count != eventTypes.Count)
        {
            throw new ArgumentException("Events and event types must contain the same number of entries.");
        }

        if (options is not null && options.Count != events.Count)
        {
            throw new ArgumentException("Options must contain the same number of entries as events when supplied.");
        }

        if (events.Count == 0)
        {
            return Array.Empty<OutboxReceipt>();
        }

        var receipts = new OutboxReceipt[events.Count];

        for (var index = 0; index < events.Count; index++)
        {
            var @event = events[index];
            var eventType = eventTypes[index];
            var itemOptions = options?[index] ?? new OutboxOptions();

            ArgumentNullException.ThrowIfNull(@event);
            ArgumentNullException.ThrowIfNull(eventType);

            if (!eventType.IsInstanceOfType(@event))
            {
                throw new ArgumentException(
                    $"Event at index {index} is not assignable to '{eventType.FullName}'.",
                    nameof(events));
            }

            var contract = _contractRegistry.GetContract(eventType);
            var storedAt = _clock.GetUtcNow();
            var messageId = itemOptions.Id ?? Guid.NewGuid();
            var payload = await _messageSerializer.SerializeAsync(@event, cancellationToken).ConfigureAwait(false);
            payload = await ProtectPayloadAsync(payload, cancellationToken).ConfigureAwait(false);

            var envelope = new OutboxEnvelope
            {
                Id = messageId,
                ContractName = contract.Name,
                ContractVersion = contract.Version,
                Payload = payload,
                Topic = itemOptions.Topic,
                CreatedAt = storedAt,
                VisibleAfter = itemOptions.VisibleAfter,
                Status = OutboxStatus.Pending,
                AttemptCount = 0,
                CorrelationId = itemOptions.CorrelationId,
                CausationId = itemOptions.CausationId,
                TenantId = itemOptions.TenantId,
                IdempotencyKey = string.IsNullOrWhiteSpace(itemOptions.IdempotencyKey) ? null : itemOptions.IdempotencyKey,
                TraceContext = itemOptions.TraceContext
            };

            _interceptor.Enqueue(_dbContext, envelope);

            receipts[index] = new OutboxReceipt
            {
                Id = envelope.Id,
                MessageType = eventType,
                ContractName = envelope.ContractName,
                ContractVersion = envelope.ContractVersion,
                StoredAt = envelope.CreatedAt,
                CorrelationId = envelope.CorrelationId,
                CausationId = envelope.CausationId,
                TenantId = envelope.TenantId
            };
        }

        return receipts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxReceipt<TEvent>>> EnqueueBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        IReadOnlyList<OutboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(events);

        if (options is not null && options.Count != events.Count)
        {
            throw new ArgumentException("Options must contain the same number of entries as events when supplied.");
        }

        if (events.Count == 0)
        {
            return Array.Empty<OutboxReceipt<TEvent>>();
        }

        var receipts = new OutboxReceipt<TEvent>[events.Count];

        for (var index = 0; index < events.Count; index++)
        {
            receipts[index] = await EnqueueAsync(events[index], options?[index], cancellationToken).ConfigureAwait(false);
        }

        return receipts;
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> ScheduleAsync<TEvent>(
        TEvent @event,
        DateTimeOffset enqueueAt,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return EnqueueAsync(@event, WithVisibleAfter(options, enqueueAt), cancellationToken);
    }

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> ScheduleAfterAsync<TEvent>(
        TEvent @event,
        TimeSpan delay,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, nameof(delay));

        return EnqueueAsync(@event, WithVisibleAfter(options, _clock.GetUtcNow().Add(delay)), cancellationToken);
    }

    /// <summary>
    ///     Encrypts a serialized payload when an outbox protector is configured.
    /// </summary>
    /// <param name="payload">The serialized payload.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The protected payload text.</returns>
    private Task<string> ProtectPayloadAsync(string payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return _payloadProtector is null
            ? Task.FromResult(payload)
            : _payloadProtector.EncryptAsync(payload, cancellationToken);
    }

    /// <summary>
    ///     Merges the supplied options with a scheduled visibility timestamp.
    /// </summary>
    /// <param name="options">The caller-supplied outbox options, if any.</param>
    /// <param name="visibleAfter">The UTC timestamp when the event becomes visible to processors.</param>
    /// <returns>Outbox options with <see cref="OutboxOptions.VisibleAfter" /> set.</returns>
    private static OutboxOptions WithVisibleAfter(OutboxOptions? options, DateTimeOffset visibleAfter)
    {
        options ??= new OutboxOptions();

        return options with { VisibleAfter = visibleAfter };
    }
}
