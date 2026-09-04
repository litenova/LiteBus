# Reliable Messaging Semantics

**Production tier: GA**

Deep reference for delivery guarantees, idempotency, and broker behavior in LiteBus. For registration and package layout, start with [Reliable Messaging](README.md). For atomic domain and inbox/outbox writes, see [Transactional Messaging Writes](transactional-writes.md).

## At-Least-Once

LiteBus durable paths persist before execution (inbox) or before broker publish completes terminal state (outbox). A worker may therefore run the same logical message more than once after crash, lease expiry, or broker redelivery.

Applications must make handlers **idempotent** or deduplicate using:

* `InboxAcceptMetadata.Idempotency` / `OutboxEnqueueMetadata.Idempotency` (`Idempotency.Keyed`)
* Natural keys in domain stores
* Broker message IDs where consumers support dedup

## Inbox Accept vs Execute

| Step | API | Committed when |
| --- | --- | --- |
| Accept | `IInbox.AcceptAsync` | Row visible in store (Pending) |
| Execute | `PipelinedInboxProcessor` + dispatcher | Terminal status after successful dispatch |

Accept is durable once the store transaction commits. Execution is at-least-once under lease.

## Outbox Enqueue vs Publish

| Step | API | Committed when |
| --- | --- | --- |
| Enqueue | `IOutbox.EnqueueAsync` | Row in store (often same UoW as domain change) |
| Publish | Processor + `IOutboxDispatcher` | Terminal Published/Failed/DeadLettered |

Transactional outbox: enqueue in the same database transaction as domain entities. EF uses the save-changes interceptor; PostgreSQL uses `ITransactionalOutbox` with ambient provider or manual bind. See [Transactional messaging writes](transactional-writes.md) and [Domain events and unit of work](../concepts/domain-events-and-unit-of-work.md).

## Lease Recovery

Processors lease batches with `lease_owner` and `lease_expires_at`. Heartbeat renews leases during long handlers. When lease expires:

* Another worker may lease the same row
* Terminal persist uses conditional update (`lease_owner` match) to avoid overwriting a newer attempt

PostgreSQL and EF Core stores use atomic conditional persist.

## Drain Before Stop

Hosted services run `IBackgroundService` processor loops. On shutdown:

1. Drain control stops new leases
2. In-flight dispatches complete (subject to cancellation token policy)
3. `HonorShutdownTokenOnPersist` on processor options controls whether cancel aborts terminal persist (default: complete persist)

See [Hosted services](../architecture/hosted-services.md) and [Production runbook](../operations/runbook.md).

## Broker Ingress Ack Policy

| Broker | Commit/ack timing | Failure before accept |
| --- | --- | --- |
| Kafka | Offset commit on ack | Seek back; no commit |
| AMQP | BasicAck after accept | Nack requeue or reject |
| SQS | Delete on ack | Visibility timeout extension |
| Azure | Complete/abandon per SDK | Abandon for retry |

Ingress logs `ingress.ack_failed_after_accept` when ack fails after successful accept; broker may redeliver (at-least-once).

## Schema Version 1

PostgreSQL inbox, outbox, and saga tables use schema version **1**. The create scripts contain the complete current shape. The schema manager validates existing tables and does not mutate incompatible shapes. See the [Migration Guide](../migration/v6.md) for historical database transitions.

## Comparison Table

| Concern | Direct mediation | Durable inbox/outbox |
| --- | --- | --- |
| Crash safety | None | Store + processor |
| Ordering | Handler pipeline order | FIFO-like by `created_at`; no strict order for ties, retries, or multiple workers |
| Duplicates | None | Possible; idempotency required |
| Cross-service | Single process | Storage + optional broker |

## Tests

| Scenario | Location |
| --- | --- |
| Contract store semantics | `LiteBus.Storage.Testing` |
| Concurrent lease / persist | PostgreSQL + EF integration tests |
| Ingress requeue | Broker integration tests under `LiteBus.Composition.UnitTests` |

## Related Docs

* [Inbox](inbox.md), [Outbox](outbox.md)
* [Custom stores and dispatchers](../extending/custom-stores-and-dispatchers.md)
