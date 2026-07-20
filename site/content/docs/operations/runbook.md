# Production Runbook

**Production tier: GA**

On-call reference for LiteBus durable messaging. It assumes PostgreSQL storage and AMQP or direct mediator dispatch unless noted.

## Quick Triage

| Signal | Likely cause | First action |
| --- | --- | --- |
| Inbox/outbox depth growing | Processor paused, store down, handler failures | Check processor pause state; store connectivity; error column |
| Rows stuck in `Processing` | Crash mid-handler, lease not renewed | Verify lease expiry; drain and replay; confirm idempotency |
| NOTIFY degraded (PostgreSQL) | Listener disconnected | Work signal falls back to poll; check logs for reconnect |
| Ingress lag | Slow accept, backpressure | Scale consumers; check store latency; prefetch settings |
| Health unhealthy | Missing probes or schema drift | List manifest probes; run schema validate |

## Stuck `Processing` Rows

1. Confirm processor is running (`EnableInboxProcessor` / hosted service started).
2. Check `lease_expires_at` and `lease_owner`. Expired leases are reclaimable on next poll.
3. POST management `.../processor/drain` (see [Operations and management](README.md)).
4. Stop host gracefully after drain completes.
5. If row remains stuck with valid lease, investigate worker holding lease; terminate worker or wait for expiry.
6. Replay from dead-letter or failed status after fixing root cause.

## Dead-Letter Replay

1. Query dead-letter rows via the management API or SQL (`status = DeadLettered`).
2. Fix the handler or downstream dependency.
3. Use `POST /litebus/inbox/messages/requeue-dead-letters` or the matching outbox route. The endpoint pages through the store manager and applies the configured operator policy.
4. Use a new idempotency key only when the application intends to create a new message. Do not edit durable rows directly.
5. Monitor attempt_count and last_error during replay.

## Schema Validate

```csharp
await schemaManager.ValidateAsync(cancellationToken);
```

Failure indicates drift from the current version 1 component schema. LiteBus does not mutate an incompatible table. See [PostgreSQL Schema Management](../integrations/postgresql-schema-management.md) for validation and the [Migration Guide](../migration/v6.md) for historical table transitions.

## Processor Drain Sequence (Deployments)

1. Enable maintenance mode at load balancer (stop new HTTP traffic).
2. POST `/litebus/inbox/processor/drain` and `/litebus/outbox/processor/drain`.
3. Wait until in-flight count zero (metrics or store query).
4. Stop application host.
5. Deploy new version; run schema ensure if needed.
6. Start host; verify health and probe checks.

## NOTIFY Degraded Mode

PostgreSQL work signals listen on channel names registered by inbox/outbox storage. When the listener connection drops, `PostgreSqlWorkSignal` reconnects after a short delay and falls back to poll interval until notifications resume.

| Symptom | Action |
| --- | --- |
| Higher poll latency only | Acceptable temporarily; monitor reconnect logs |
| Repeated reconnect failures | Check PostgreSQL max connections, firewall, PgBouncer transaction pooling mode |

## Broker Comparison (Ingress Failure)

| Broker | Transient accept failure | Poison message |
| --- | --- | --- |
| AMQP | Nack with requeue (unless shutdown cancel) | Reject without requeue or DLX policy |
| Kafka | Seek to offset; no commit | Skip commit; fix handler; may require manual offset |
| SQS | Visibility timeout backoff | Delete or DLQ when `RequeueOnFailure = false` |
| Azure Service Bus | Abandon/defer per adapter | Dead-letter subqueue |

See broker docs: [AMQP](../integrations/amqp.md), [Kafka](../integrations/kafka.md), [AWS SQS](../integrations/aws-sqs.md), [Azure Service Bus](../integrations/azure-service-bus.md).

## Escalation Data to Collect

* LiteBus package versions and `LiteBusHostManifest` probe list
* Sample stuck row (`message_id`, status, lease fields, last_error)
* Processor options (batch size, lease duration, heartbeat interval)
* Recent deploy and migration changes

## Tests Proving Runbook Scenarios

| Scenario | Test home |
| --- | --- |
| Drain then stop | `LiteBus.Transport.IntegrationTesting` component hosts |
| Lease reclaim | `LiteBus.Storage.PostgreSql.IntegrationTests` stress tests |
| NOTIFY reconnect | `PostgreSqlInboxWorkSignalTests`, `PostgreSqlOutboxWorkSignalTests` |
| Ingress ack after accept | `LiteBus.Inbox.Ingress.UnitTests` |

## Related Docs

* [Reliable messaging](../reliable-messaging/README.md)
* [Troubleshooting](troubleshooting.md)
* [Operations and management](README.md)
