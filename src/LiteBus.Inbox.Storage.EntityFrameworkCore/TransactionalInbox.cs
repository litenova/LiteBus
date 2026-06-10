using System;

using System.Collections.Generic;

using System.Threading;

using System.Threading.Tasks;

using LiteBus.Inbox.Abstractions;

using LiteBus.Messaging.Abstractions;

using Microsoft.EntityFrameworkCore;



namespace LiteBus.Inbox.Storage.EntityFrameworkCore;



/// <summary>

///     Accepts messages through <see cref="LiteBusInboxSaveChangesInterceptor" /> so contract resolution and

///     serialization follow the same path as <see cref="IInbox" /> while rows commit with the active

///     <see cref="DbContext" /> transaction.

/// </summary>

/// <typeparam name="TContext">The application database context type bound to the current scope.</typeparam>

public sealed class TransactionalInbox<TContext> : ITransactionalInbox<TContext>

    where TContext : DbContext

{

    /// <summary>

    ///     Gets the time provider used to stamp storage time on staged envelopes.

    /// </summary>

    private readonly TimeProvider _clock;



    /// <summary>

    ///     Gets the registry used to map the runtime message type to a stable contract.

    /// </summary>

    private readonly IContractReader _contractRegistry;



    /// <summary>

    ///     Gets the database context that owns the ambient transaction for staged envelopes.

    /// </summary>

    private readonly TContext _dbContext;



    /// <summary>

    ///     Gets the interceptor that queues envelopes until the next <c>SaveChanges</c> call.

    /// </summary>

    private readonly LiteBusInboxSaveChangesInterceptor _interceptor;



    /// <summary>

    ///     Gets the serializer used to create the serialized payload.

    /// </summary>

    private readonly IMessageSerializer _messageSerializer;



    /// <summary>

    ///     Gets the optional inbox protector applied before payloads are staged.

    /// </summary>

    private readonly IInboxPayloadProtector? _payloadProtector;



    /// <summary>

    ///     Initializes a new instance of the <see cref="TransactionalInbox{TContext}" /> class.

    /// </summary>

    /// <param name="interceptor">The interceptor that stages envelopes for the active context.</param>

    /// <param name="dbContext">The database context that owns the ambient transaction.</param>

    /// <param name="contractRegistry">The registry used to map the runtime message type to a stable contract.</param>

    /// <param name="messageSerializer">The serializer used to create the serialized payload.</param>

    /// <param name="clock">The time provider used to stamp storage time on staged envelopes.</param>

    /// <param name="payloadProtector">The optional inbox protector applied before payloads are staged.</param>

    public TransactionalInbox(

        LiteBusInboxSaveChangesInterceptor interceptor,

        TContext dbContext,

        IContractReader contractRegistry,

        IMessageSerializer messageSerializer,

        TimeProvider clock,

        IInboxPayloadProtector? payloadProtector = null)

    {

        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));

        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));

        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));

        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        _payloadProtector = payloadProtector;

    }



    /// <inheritdoc />

    public async Task<InboxReceipt> AcceptAsync(

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



        var contract = _contractRegistry.GetContract(messageType);

        var acceptedAt = _clock.GetUtcNow();

        var messageId = options.Id ?? Guid.NewGuid();

        var payload = await _messageSerializer.SerializeAsync(message, cancellationToken).ConfigureAwait(false);

        payload = await ProtectPayloadAsync(payload, cancellationToken).ConfigureAwait(false);



        var envelope = new InboxEnvelope

        {

            Id = messageId,

            ContractName = contract.Name,

            ContractVersion = contract.Version,

            Payload = payload,

            CreatedAt = acceptedAt,

            VisibleAfter = options.VisibleAfter,

            Status = InboxStatus.Pending,

            AttemptCount = 0,

            CorrelationId = options.CorrelationId,

            CausationId = options.CausationId,

            TenantId = options.TenantId,

            IdempotencyKey = string.IsNullOrWhiteSpace(options.IdempotencyKey) ? null : options.IdempotencyKey,

            TraceContext = options.TraceContext

        };



        _interceptor.Enqueue(_dbContext, envelope);



        return new InboxReceipt

        {

            Id = envelope.Id,

            MessageType = messageType,

            ContractName = envelope.ContractName,

            ContractVersion = envelope.ContractVersion,

            AcceptedAt = envelope.CreatedAt,

            CorrelationId = envelope.CorrelationId,

            CausationId = envelope.CausationId,

            TenantId = envelope.TenantId

        };

    }



    /// <inheritdoc />

    public async Task<InboxReceipt<T>> AcceptAsync<T>(

        T message,

        InboxOptions? options = null,

        CancellationToken cancellationToken = default)

        where T : notnull

    {

        var receipt = await AcceptAsync(message, message.GetType(), options, cancellationToken).ConfigureAwait(false);



        return new InboxReceipt<T>

        {

            Id = receipt.Id,

            MessageType = receipt.MessageType,

            ContractName = receipt.ContractName,

            ContractVersion = receipt.ContractVersion,

            AcceptedAt = receipt.AcceptedAt,

            CorrelationId = receipt.CorrelationId,

            CausationId = receipt.CausationId,

            TenantId = receipt.TenantId

        };

    }



    /// <inheritdoc />

    public async Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(

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

            return Array.Empty<InboxReceipt>();

        }



        var receipts = new InboxReceipt[messages.Count];



        for (var index = 0; index < messages.Count; index++)

        {

            receipts[index] = await AcceptAsync(

                messages[index],

                messageTypes[index],

                options?[index],

                cancellationToken).ConfigureAwait(false);

        }



        return receipts;

    }



    /// <inheritdoc />

    public async Task<IReadOnlyList<InboxReceipt<T>>> AcceptBatchAsync<T>(

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

            return Array.Empty<InboxReceipt<T>>();

        }



        var receipts = new InboxReceipt<T>[messages.Count];



        for (var index = 0; index < messages.Count; index++)

        {

            receipts[index] = await AcceptAsync(messages[index], options?[index], cancellationToken).ConfigureAwait(false);

        }



        return receipts;

    }



    /// <inheritdoc />

    public Task<InboxReceipt<T>> ScheduleAsync<T>(

        T message,

        DateTimeOffset enqueueAt,

        InboxOptions? options = null,

        CancellationToken cancellationToken = default)

        where T : notnull

    {

        return AcceptAsync(message, WithVisibleAfter(options, enqueueAt), cancellationToken);

    }



    /// <inheritdoc />

    public Task<InboxReceipt<T>> ScheduleAfterAsync<T>(

        T message,

        TimeSpan delay,

        InboxOptions? options = null,

        CancellationToken cancellationToken = default)

        where T : notnull

    {

        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero, nameof(delay));



        return AcceptAsync(message, WithVisibleAfter(options, _clock.GetUtcNow().Add(delay)), cancellationToken);

    }



    /// <summary>

    ///     Encrypts a serialized payload when an inbox protector is configured.

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

    /// <param name="options">The caller-supplied inbox options, if any.</param>

    /// <param name="visibleAfter">The UTC timestamp when the message becomes visible to processors.</param>

    /// <returns>Inbox options with <see cref="InboxOptions.VisibleAfter" /> set.</returns>

    private static InboxOptions WithVisibleAfter(InboxOptions? options, DateTimeOffset visibleAfter)

    {

        options ??= new InboxOptions();



        return options with { VisibleAfter = visibleAfter };

    }

}


