# Custom Stores and Dispatchers

The inbox and outbox modules define storage as narrow interfaces and ship PostgreSQL, EF Core, and InMemory implementations. When you need a different database, or want the outbox to publish to a message broker instead of in-process handlers, you implement these roles yourself. Read [Reliable Messaging](../reliable-messaging/README.md) first.

## Custom Stores and Transactional Writes

You do not reimplement contract resolution or serialization. Implement the store roles and let callers use `IInboxEnvelopeFactory` / `IOutboxEnvelopeFactory` with `StoreBoundTransactionalInbox` / `StoreBoundTransactionalOutbox`, or expose `ITransactionalInboxStore` / `ITransactionalOutboxStore` that writes inside the caller's transaction without committing independently.

Participation patterns and registration examples: [Transactional messaging writes](../reliable-messaging/transactional-writes.md).

## Why the Roles Are Split

Acceptance code, the leasing processor, and result recording are separated so callers depend on minimal surfaces. A single class may implement all roles against one table, which is what built-in stores do.

## Inbox Store Roles

| Role | Used by | Responsibility |
| --- | --- | --- |
| `IInboxStore` | `IInbox.AcceptAsync` | Append a pending inbox envelope. On duplicate idempotency key, return the existing row. |
| `IInboxLeaseStore` | `PipelinedInboxProcessor` | Atomically claim due envelopes for one worker. |
| `IInboxStateWriter` | `PipelinedInboxProcessor` | `PersistAsync` terminal outcomes (completed, retry, dead-letter). |

The writer receives envelopes with identifier, contract name and version, payload, visibility, idempotency key, and tracing metadata already assigned. Write inside the caller's transaction boundary when one exists.

## Outbox Store Roles

| Role | Used by | Responsibility |
| --- | --- | --- |
| `IOutboxStore` | `IOutbox.EnqueueAsync` | Append a pending outbox envelope. |
| `IOutboxLeaseStore` | `PipelinedOutboxProcessor` | Atomically claim due messages. |
| `IOutboxStateWriter` | `PipelinedOutboxProcessor` | `PersistAsync` published, retry, or dead-letter state. |

## Dispatchers

```csharp
public interface IInboxDispatcher
{
    Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default);
}

public interface IOutboxDispatcher
{
    Task DispatchAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default);
}
```

Register built-in dispatchers through nested `Use*` extensions:

| Path | In-process | External broker |
| --- | --- | --- |
| Inbox | `UseInProcessDispatch()`: `LiteBus.Inbox.Dispatch.InProcess` | Root `AddAmqpTransport(...)` plus `UseAmqpDispatch(...)`: `LiteBus.Inbox.Dispatch.Amqp`; root `AddInMemoryTransport()` plus `UseInMemoryDispatch(...)`: `LiteBus.Inbox.Dispatch.InMemory` |
| Outbox | `UseInProcessDispatch()`: `LiteBus.Outbox.Dispatch.InProcess` | Root `AddAmqpTransport(...)` plus `UseAmqpDispatch(...)`: `LiteBus.Outbox.Dispatch.Amqp`; root `AddInMemoryTransport()` plus `UseInMemoryDispatch(...)`: `LiteBus.Outbox.Dispatch.InMemory` |

Throw when dispatch fails; the processor records retry or dead-letter state from the exception.

## Registering Custom Implementations

Register store roles and dispatchers in DI, then reference them from custom module code or replace built-in registrations:

```csharp
builder.AddInbox(inbox =>
{
    inbox.Contracts.Register<MyCommand>("my.command", 1);
    // custom store modules register IInboxStore / IInboxLeaseStore / IInboxStateWriter
    inbox.UseInProcessDispatch();
    inbox.EnableInboxProcessor();
});
```

## Guarantees Your Implementation Must Hold

- **Idempotent append.** Repeated idempotency key returns the existing row.
- **Atomic lease.** Two workers must never hold the same row at once.
- **Reclaimable expiry.** Expired leases become claimable again.
- **Terminal persist.** `PersistAsync` returns explicit outcomes when lease is lost (see `PersistResult`).

## Next

Read [Hosted services](../architecture/hosted-services.md) and [Troubleshooting](../operations/troubleshooting.md).
