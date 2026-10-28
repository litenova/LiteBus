# Reliable Messaging Semantics

**Production tier: GA**

LiteBus durable messaging delivers each message at-least-once under leases. This page defines the delivery guarantees, idempotency rules, and broker behavior.

[Ordering and Concurrency Guarantees](ordering.md) defines the concurrency properties, and [Message States](../operations/message-states.md) defines the operator states and alerts. For registration and package layout, start with [Reliable Messaging](README.md). For atomic domain and inbox/outbox writes, see [Transactional Messaging Writes](transactional-writes.md).

## At-Least-Once

LiteBus durable paths persist before execution (inbox) or before broker publish completes terminal state (outbox). After a crash, lease expiry, or broker redelivery, a worker may therefore run the same logical message more than once.

Applications must make handlers **idempotent** or deduplicate using:

- `InboxAcceptMetadata.Idempotency` / `OutboxEnqueueMetadata.Idempotency` (`Idempotency.Keyed`)
- Natural keys in domain stores
- Broker message IDs where consumers support deduplication

## Inbox Accept vs Execute

| Step | API | Committed when |
| --- | --- | --- |
| Accept | `IInbox.AcceptAsync` | Row visible in store (Pending) |
| Execute | `PipelinedInboxProcessor` + dispatcher | Terminal status after successful dispatch |

Once the store transaction commits, Accept is durable. Execution is at-least-once under a lease.

## Outbox Enqueue vs Publish

| Step | API | Committed when |
| --- | --- | --- |
| Enqueue | `IOutbox.EnqueueAsync` | Row in store (often same unit of work as domain change) |
| Publish | Processor + `IOutboxDispatcher` | Terminal Published/Failed/DeadLettered |

With the transactional outbox, enqueue in the same database transaction as domain entities. EF Core uses the save-changes interceptor. PostgreSQL uses `ITransactionalOutbox` with ambient provider or manual bind. See [Transactional Messaging Writes](transactional-writes.md) and [Domain Events and Unit of Work](domain-events-and-unit-of-work.md).

## Lease Recovery

Processors lease batches with `lease_owner` and `lease_expires_at`. During long handlers, heartbeat renews leases. When a lease expires:

- Another worker may lease the same row
- Terminal persist uses conditional update (`lease_owner` match) to avoid overwriting a newer attempt

PostgreSQL and EF Core stores use atomic conditional persist.

When the processor cancels a dispatch itself during shutdown, it releases the lease through `ILeaseRenewable.ReleaseLeaseAsync`. The release returns the row to `Pending` (or `Failed` when it already carries an error) with the attempt count decremented, instead of spending an attempt. The InMemory, PostgreSQL, and EF Core inbox and outbox stores implement the method.

Drain never cancels dispatch. The final pass runs with the uncancelled host token, so in-flight messages complete and persist normally.

## Drain Before Stop

Hosted services run `IBackgroundService` processor loops. On shutdown:

1. Drain control stops new leases
2. In-flight dispatches complete (subject to cancellation token policy)
3. `HonorShutdownTokenOnPersist` on processor options controls whether cancel aborts terminal persist (default: complete persist)

See [Hosted Services](../architecture/hosted-services.md) and [Production Runbook](../operations/runbook.md).

## Broker Ingress Ack Policy

| Broker | Commit/ack timing | Failure before accept |
| --- | --- | --- |
| Kafka | Offset commit on ack | Seek back without committing |
| AMQP | BasicAck after accept | Nack requeue or reject |
| SQS | Delete on ack | Visibility timeout extension |
| Azure | Complete/abandon per SDK | Abandon for retry |

Ingress logs `ingress.ack_failed_after_accept` when ack fails after successful accept. The broker may then redeliver (at-least-once).

## Schema Version 1

PostgreSQL inbox, outbox, and saga tables use schema version **1**. The create scripts contain the complete current shape. The schema manager validates existing tables and does not mutate incompatible shapes. See the [Migration Guide](../migration/v6.md) for historical database transitions.

## Comparison Table

| Concern | Direct mediation | Durable inbox/outbox |
| --- | --- | --- |
| Crash safety | None | Store + processor |
| Ordering | Handler pipeline order | FIFO-like by `created_at`, with no strict order for ties, retries, or multiple workers |
| Duplicates | None | Possible, requiring idempotency |
| Cross-service | Single process | Storage + optional broker |

## Tests

| Scenario | Location |
| --- | --- |
| Contract store semantics | `LiteBus.Storage.Testing` |
| Concurrent lease / persist | PostgreSQL + EF Core integration tests |
| Ingress requeue | Broker ingress requeue scenarios in `tests/LiteBus.Durable.IntegrationTests/Ingress/` |

## Related Docs

- [Inbox](inbox.md), [Outbox](outbox.md)
- [Custom Stores and Dispatchers](../extending/custom-stores-and-dispatchers.md)
