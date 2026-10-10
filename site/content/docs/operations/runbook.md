# Production Runbook

This page is the on-call reference for LiteBus durable messaging. It assumes PostgreSQL storage and AMQP or direct mediator dispatch unless noted.

> **This page is for** operators who triage inbox and outbox backlogs, stuck leases, and deploy-time drains.
>
> **After reading it** you can drain a processor, replay dead letters, and validate schema state. Continue with [Troubleshooting](troubleshooting.md).

## Triage Signals Fast

| Signal | Likely cause | First action |
| :--- | :--- | :--- |
| Inbox or outbox depth growing | Processor paused, store down, handler failures | Check processor pause state, store connectivity, and the error column |
| Rows stuck in `Processing` | Crash mid-handler with lease not renewed | Verify lease expiry, then drain and replay |
| NOTIFY degraded on PostgreSQL | Listener disconnected | Accept poll fallback and check reconnect logs |
| Ingress lag | Slow accept with backpressure | Scale consumers and check store latency and prefetch |
| Health unhealthy | Missing probes or schema drift | List manifest probes and run schema validate |

## Free Stuck Processing Rows

1. Confirm the processor is running through `EnableInboxProcessor` or the hosted service state.
2. Check `lease_expires_at` and `lease_owner`. Expired leases are reclaimable on the next poll.
3. Post to the management drain route described in [Operations and Management](README.md).
4. Stop the host gracefully after drain completes.
5. If a row stays stuck with a valid lease, find the worker that holds the lease, end that worker or wait for expiry.
6. Replay from dead-letter or failed status after you fix the root cause.

Confirm idempotency before you replay: the processor runs at least once, so a replayed handler must tolerate a second execution.

## Replay Dead Letters

1. Query dead-letter rows through the management API or SQL with `status = DeadLettered`.
2. Fix the handler or the downstream dependency first.
3. Post to `/litebus/inbox/messages/requeue-dead-letters` or the matching outbox route. The endpoint pages through the store manager and applies the configured operator policy.
4. Use a new idempotency key only when your application intends to create a new message. Do not edit durable rows directly.
5. Watch `attempt_count` and `last_error` during replay.

## Validate Schema State

```csharp
await schemaManager.ValidateAsync(cancellationToken);
```

A failure means the table drifted from the current version 1 component schema. LiteBus does not mutate an incompatible table. See [PostgreSQL Schema Management](../integrations/postgresql-schema-management.md) for validation steps. Production deploys should apply DDL and call `ValidateAsync` in deploy checks.

## Drain Before Deploy

1. Enable maintenance mode at the load balancer to stop new HTTP traffic.
2. Post to `/litebus/inbox/processor/drain` and `/litebus/outbox/processor/drain`.
3. Wait until the in-flight count reaches zero through metrics or a store query.
4. Stop the application host.
5. Deploy the new version and run schema ensure when needed.
6. Start the host and verify health and probe checks.

## Handle Notify Degraded Mode

PostgreSQL work signals listen on channel names registered by inbox and outbox storage. When the listener connection drops, `PostgreSqlWorkSignal` reconnects after a short delay and falls back to the poll interval until notifications resume.

| Symptom | Action |
| :--- | :--- |
| Higher poll latency only | Accept it temporarily and monitor reconnect logs |
| Repeated reconnect failures | Check PostgreSQL max connections, firewall rules, and PgBouncer transaction pooling mode |

## Handle Broker Ingress Failure

| Broker | Transient accept failure | Poison message |
| :--- | :--- | :--- |
| AMQP | Nack with requeue unless shutdown cancelled | Reject without requeue or use DLX policy |
| Kafka | Seek to offset with no commit | Skip commit, fix the handler, and set the offset manually when needed |
| SQS | Visibility timeout backoff | Delete or send to DLQ when `RequeueOnFailure` is false |
| Azure Service Bus | Abandon or defer per adapter | Move to the dead-letter subqueue |

See the broker guides: [AMQP](../integrations/amqp.md), [Kafka](../integrations/kafka.md), [AWS SQS](../integrations/aws-sqs.md), and [Azure Service Bus](../integrations/azure-service-bus.md).

## Collect Escalation Data

- LiteBus package versions and the `LiteBusHostManifest` probe list
- One sample stuck row with `message_id`, status, lease fields, and `last_error`
- Processor options with batch size, lease duration, and heartbeat interval
- Recent deploy and migration changes

## Details

| Scenario | Test home |
| :--- | :--- |
| Drain then stop | `LiteBus.Transport.IntegrationTesting` component hosts |
| Lease reclaim | `LiteBus.Storage.PostgreSql.IntegrationTests` stress tests |
| NOTIFY reconnect | `PostgreSqlInboxWorkSignalTests` and `PostgreSqlOutboxWorkSignalTests` |
| Ingress ack after accept | `LiteBus.Inbox.Ingress.UnitTests` |

## Next

- [Reliable Messaging](../reliable-messaging/README.md)
- [Troubleshooting](troubleshooting.md)
- [Operations and Management](README.md)
