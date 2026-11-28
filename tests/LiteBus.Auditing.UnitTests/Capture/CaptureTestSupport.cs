using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using LiteBus.Auditing.Capture;
using LiteBus.Messaging.Abstractions;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Auditing.UnitTests.Capture;

/// <summary>
///     An in-memory audit trail that records everything written to it.
/// </summary>
internal sealed class RecordingAuditTrail : IAuditTrail
{
    /// <summary>
    ///     Gets the records written during the test.
    /// </summary>
    public ConcurrentQueue<AuditRecord> Records { get; } = new();

    /// <inheritdoc />
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        Records.Enqueue(record);
        return Task.CompletedTask;
    }
}

/// <summary>
///     A transactional outbox that records enqueued messages instead of storing them.
/// </summary>
internal sealed class RecordingAuditOutbox : ITransactionalOutbox
{
    /// <summary>
    ///     Gets the audit messages enqueued during the test, in enqueue order.
    /// </summary>
    public List<OutboxEnqueueItem<AuditRecordOutboxMessage>> AuditItems { get; } = [];

    /// <summary>
    ///     Gets the untyped items enqueued during the test, in enqueue order.
    /// </summary>
    public List<OutboxEnqueueItem> Items { get; } = [];

    /// <inheritdoc />
    public Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        OutboxEnqueueItem<TEvent> item,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        if (item is OutboxEnqueueItem<AuditRecordOutboxMessage> audit)
        {
            AuditItems.Add(audit);
        }

        var messageType = item.Message.GetType();

        return Task.FromResult(new OutboxReceipt<TEvent>
        {
            Id = ReceiptId(item.Metadata),
            MessageType = messageType,
            Contract = new MessageContractReference
            {
                Name = messageType.FullName ?? messageType.Name,
                Version = 1
            },
            StoredAt = DateTimeOffset.UtcNow,
            Trace = item.Metadata.Trace,
            Tenant = item.Metadata.Tenant
        });
    }

    /// <inheritdoc />
    public Task<OutboxReceipt> EnqueueAsync(OutboxEnqueueItem item, CancellationToken cancellationToken)
    {
        Items.Add(item);

        return Task.FromResult(new OutboxReceipt
        {
            Id = ReceiptId(item.Metadata),
            MessageType = item.MessageType,
            Contract = new MessageContractReference
            {
                Name = item.MessageType.FullName ?? item.MessageType.Name,
                Version = 1
            },
            StoredAt = DateTimeOffset.UtcNow,
            Trace = item.Metadata.Trace,
            Tenant = item.Metadata.Tenant
        });
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(
        IReadOnlyList<OutboxEnqueueItem> items,
        CancellationToken cancellationToken)
    {
        var receipts = new List<OutboxReceipt>(items.Count);

        foreach (var item in items)
        {
            Items.Add(item);
            receipts.Add(new OutboxReceipt
            {
                Id = ReceiptId(item.Metadata),
                MessageType = item.MessageType,
                Contract = new MessageContractReference
                {
                    Name = item.MessageType.FullName ?? item.MessageType.Name,
                    Version = 1
                },
                StoredAt = DateTimeOffset.UtcNow,
                Trace = item.Metadata.Trace,
                Tenant = item.Metadata.Tenant
            });
        }

        return Task.FromResult<IReadOnlyList<OutboxReceipt>>(receipts);
    }

    private static Guid ReceiptId(OutboxEnqueueMetadata metadata)
    {
        return metadata.Identity is MessageIdentity.Supplied supplied ? supplied.Value : Guid.NewGuid();
    }
}

/// <summary>
///     Test metadata keyed by value type.
/// </summary>
internal sealed class FakeMessageMetadata : IMessageMetadata
{
    private readonly Dictionary<Type, object> _values = new();

    /// <inheritdoc />
    public IReadOnlyCollection<object> Values => _values.Values;

    /// <inheritdoc />
    public bool TryGet<TValue>([MaybeNullWhen(false)] out TValue value)
        where TValue : notnull
    {
        if (_values.TryGetValue(typeof(TValue), out var stored) && stored is TValue typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc />
    public bool Contains<TValue>()
        where TValue : notnull
    {
        return _values.ContainsKey(typeof(TValue));
    }

    /// <summary>
    ///     Stores a value under its own type.
    /// </summary>
    public FakeMessageMetadata Set<TValue>(TValue value)
        where TValue : notnull
    {
        _values[typeof(TValue)] = value;
        return this;
    }
}

/// <summary>
///     A test message descriptor carrying only its type and metadata.
/// </summary>
internal sealed class FakeMessageDescriptor : IMessageDescriptor
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeMessageDescriptor" /> class.
    /// </summary>
    public FakeMessageDescriptor(Type messageType, IMessageMetadata metadata)
    {
        MessageType = messageType;
        Metadata = metadata;
    }

    /// <inheritdoc />
    public Type MessageType { get; }

    /// <inheritdoc />
    public bool IsGeneric => false;

    /// <inheritdoc />
    public IMessageMetadata Metadata { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<IMainHandlerDescriptor> Handlers => [];

    /// <inheritdoc />
    public IReadOnlyCollection<IMainHandlerDescriptor> IndirectHandlers => [];

    /// <inheritdoc />
    public IReadOnlyCollection<IPostHandlerDescriptor> PostHandlers => [];

    /// <inheritdoc />
    public IReadOnlyCollection<IPostHandlerDescriptor> IndirectPostHandlers => [];

    /// <inheritdoc />
    public IReadOnlyCollection<IPreStageHandlerDescriptor> PreStageHandlers => [];

    /// <inheritdoc />
    public IReadOnlyCollection<IPreStageHandlerDescriptor> IndirectPreStageHandlers => [];

    /// <inheritdoc />
    public IReadOnlyCollection<ICompletionHandlerDescriptor> CompletionHandlers => [];

    /// <inheritdoc />
    public IReadOnlyCollection<ICompletionHandlerDescriptor> IndirectCompletionHandlers => [];

    /// <inheritdoc />
    public IReadOnlyCollection<IErrorHandlerDescriptor> ErrorHandlers => [];

    /// <inheritdoc />
    public IReadOnlyCollection<IErrorHandlerDescriptor> IndirectErrorHandlers => [];

    /// <inheritdoc />
    public IReadOnlyCollection<IRefusalMapperDescriptor> RefusalMappers => [];

    /// <inheritdoc />
    public IReadOnlyCollection<IRefusalMapperDescriptor> IndirectRefusalMappers => [];
}

/// <summary>
///     A test message registry holding descriptors by exact type.
/// </summary>
internal sealed class FakeMessageReader : IMessageReader
{
    private readonly Dictionary<Type, IMessageDescriptor> _descriptors = new();

    /// <inheritdoc />
    public IReadOnlyList<IHandlerDescriptor> Handlers => [];

    /// <inheritdoc />
    public int Count => _descriptors.Count;

    /// <inheritdoc />
    public IMessageDescriptor? Find(Type messageType)
    {
        return _descriptors.TryGetValue(messageType, out var descriptor) ? descriptor : null;
    }

    /// <summary>
    ///     Adds a message with the given metadata.
    /// </summary>
    public FakeMessageReader Add(Type messageType, IMessageMetadata metadata)
    {
        _descriptors[messageType] = new FakeMessageDescriptor(messageType, metadata);
        return this;
    }

    /// <inheritdoc />
    public IEnumerator<IMessageDescriptor> GetEnumerator()
    {
        return _descriptors.Values.GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

/// <summary>
///     A test execution context with a working data store.
/// </summary>
internal sealed class FakeExecutionContext : IExecutionContext
{
    /// <inheritdoc />
    public CancellationToken CancellationToken { get; set; }

    /// <inheritdoc />
    public DateTimeOffset HandledAt { get; set; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public ExecutionIdentity Identity { get; set; } = ExecutionIdentity.Anonymous;

    /// <inheritdoc />
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

    /// <inheritdoc />
    public IHandleContextData Data { get; } = new HandleContextData();

    /// <inheritdoc />
    public IReadOnlyCollection<string> Tags { get; } = [];

    /// <inheritdoc />
    public object? MessageResult { get; set; }

    /// <inheritdoc />
    public bool PostHandlersSuppressed { get; private set; }

    /// <inheritdoc />
    public void SuppressPostHandlers()
    {
        PostHandlersSuppressed = true;
    }
}

/// <summary>
///     A test audit scope holding pushed values.
/// </summary>
internal sealed class FakeAuditScope : IAuditScope
{
    /// <inheritdoc />
    public AuditActor? Actor { get; set; }

    /// <inheritdoc />
    public string? TargetId { get; set; }

    /// <inheritdoc />
    public string? Reason { get; set; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Properties { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public IAuditScope WithActor(AuditActor actor)
    {
        Actor = actor;
        return this;
    }

    /// <inheritdoc />
    public IAuditScope WithTarget(string targetId)
    {
        TargetId = targetId;
        return this;
    }

    /// <inheritdoc />
    public IAuditScope WithReason(string reason)
    {
        Reason = reason;
        return this;
    }

    /// <inheritdoc />
    public IAuditScope WithProperty(string name, string value)
    {
        var properties = new Dictionary<string, string>(Properties, StringComparer.Ordinal)
        {
            [name] = value
        };
        Properties = properties;
        return this;
    }
}

/// <summary>
///     Builds audit records for capture tests.
/// </summary>
internal static class TestAuditRecords
{
    /// <summary>
    ///     The instant every sample record carries, so tests compare fixed values.
    /// </summary>
    public static DateTimeOffset OccurredAt { get; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Builds a record with the given outcome.
    /// </summary>
    public static AuditRecord WithOutcome(AuditOutcome outcome, Guid? id = null, string? tenantId = null)
    {
        return new AuditRecord
        {
            Id = id ?? Guid.NewGuid(),
            Action = "orders.place-order",
            Outcome = outcome,
            OccurredAt = OccurredAt,
            TenantId = tenantId
        };
    }
}

/// <summary>
///     A test message with no base type.
/// </summary>
internal sealed class CaptureTestCommand;

/// <summary>
///     A test message base type carrying declarations for its derived messages.
/// </summary>
internal class CaptureTestBaseCommand;

/// <summary>
///     A test message deriving from <see cref="CaptureTestBaseCommand" />.
/// </summary>
internal sealed class CaptureTestDerivedCommand : CaptureTestBaseCommand;
