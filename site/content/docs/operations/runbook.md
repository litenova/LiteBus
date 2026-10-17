# Production Runbook

This page is the on-call reference for LiteBus durable messaging. It assumes PostgreSQL storage and AMQP or direct mediator dispatch unless noted.

> **This page is for** operators who triage inbox and outbox backlogs, stuck leases, and deploy-time drains.
>
> **After reading it** you can drain a processor, replay dead letters, and validate schema state. Continue with [Troubleshooting](troubleshooting.md).

## Triage Signals

| Signal | Likely cause | First action |
| :--- | :--- | :--- |
| Inbox or outbox depth growing | Processor paused, store down, handler failures | Check processor state, store connectivity, `last_error`, and `error_type` |
| Rows stuck in `Processing` | Worker crash, or dispatch exceeding `LeaseDuration` without heartbeat renewal | Verify lease expiry, then drain and replay |
| NOTIFY degraded on PostgreSQL | Listener disconnected | Accept poll fallback and check reconnect logs |
| Ingress lag | Slow accept with backpressure | Scale consumers and check store latency and prefetch |
| Health unhealthy | Missing probes or schema drift | List manifest probes and run schema validate |

## Control the Processor

Processor loops expose pause, resume, and drain through `IInboxProcessorControl` and `IOutboxProcessorControl`. The `State` property reports `Running`, `Paused`, or `Draining` without acquiring the processor gate. `PauseAsync` waits for the active pass to finish and is idempotent: repeated pauses and resumes are safe. `DrainAsync` runs one final pass and then stops leasing; concurrent drains await the same pass, and a drain timeout throws `TimeoutException` for the waiting caller only while the processor continues its pass. The management HTTP endpoints wrap these contracts.

```csharp
public sealed class DeployDrain
{
    private readonly IInboxProcessorControl _control;

    public DeployDrain(IInboxProcessorControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _control = control;
    }

    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        await _control.DrainAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
    }
}
```

Prefer the HTTP routes from automation and the interfaces from application code. Both report the same state; see [Management Endpoints](management-endpoints.md) for `GET .../processor/state` and the pause, resume, and drain routes.

## Free Stuck Processing Rows

1. Read processor state through `IInboxProcessorControl.State` or `GET /litebus/inbox/processor/state`. A `Paused` processor explains flat `Processing` counts.
2. Check `lease_expires_at` and `lease_owner`. Expired leases are reclaimable on the next poll.
3. Post to the management drain route described in [Management Endpoints](management-endpoints.md).
4. Stop the host gracefully after drain completes.
5. If a row stays stuck with a valid lease, find the worker that holds the lease, end that worker or wait for expiry.
6. Replay from dead-letter or failed status after you fix the root cause.

Confirm idempotency before you replay: the processor runs at least once, so a replayed handler must tolerate a second execution.

## Replay Dead Letters

1. Query dead-letter rows through the management API or SQL with `status = DeadLettered`.
2. Fix the handler or the downstream dependency first.
3. Post to `/litebus/inbox/messages/requeue-dead-letters` or the matching outbox route. The endpoint replays every dead-lettered row in pages of 200 through the store manager.
4. Use a new idempotency key only when your application intends to create a new message. Do not edit durable rows directly.
5. Watch `attempt_count` and `last_error` during replay.

## Validate Schema State

```csharp
await PostgreSqlInboxSchema.ValidateAsync(dataSource, options, cancellationToken).ConfigureAwait(false);
```

A failure means the table drifted from the current version 1 component schema. LiteBus does not mutate an incompatible table. See [PostgreSQL Schema Management](../integrations/postgresql-schema-management.md) for validation steps. Production deploys should apply DDL and call `ValidateAsync` in deploy checks.

## Drain Before Deploy

1. Enable maintenance mode at the load balancer to stop new HTTP traffic.
2. Post to `/litebus/inbox/processor/drain` and `/litebus/outbox/processor/drain`, or call `DrainAsync` on the processor controls.
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

The table lists the minimum acknowledgement and recovery behavior per adapter. Raw `IMessageConsumer` hosts use `TransportConsumerHandlerInvoker`, which returns failed deliveries to the broker and continues the consume loop. Inbox ingress applies its own poison, retry, and dead-letter policy on top of transport deliveries.

| Broker | Transient accept failure | Poison or contract failure | Consumer recovery |
| :--- | :--- | :--- | :--- |
| AMQP | Nack with requeue unless shutdown cancelled | Reject without requeue; broker DLX policy applies | Channel shutdown stops the session; the ingress loop restarts it after `RetryPollInterval` |
| Kafka | Seek to the failed offset with no commit; the record is retried in session | Discard commits the offset, skipping the record; only requeue seeks without committing | Restart or rebalance redelivers uncommitted offsets |
| SQS | `ChangeMessageVisibility` with backoff derived from `ApproximateReceiveCount` | Delete when `RequeueOnFailure` is false | Receive failures stop the session and the ingress loop restarts it; full-batch handler failures add poll backoff |
| Azure Service Bus | Abandon the message | Dead-letter to the subqueue | Exponential backoff restart loop in the consumer |
| InMemory | Re-enqueue on the channel | Release the reservation and drop the delivery | N/A (in-process) |

First actions per broker:

- AMQP: check queue depth and unacked count, then consumer connection state. Redelivered poison accumulates unless the queue has a DLX policy.
- Kafka: check consumer lag per partition and seek retry rate. A transiently failing record is seek-retried in session and holds its partition; poison is discarded and committed past. For raw consumers, handler throws seek without commit, so an uncleansed poison record does stall the partition.
- SQS: check `ApproximateAgeOfOldestMessage` and receive counts. Visibility backoff slows poison, but only a deleted message stops it.
- Azure Service Bus: check active versus dead-letter subqueue counts. Poison arrives in the dead-letter subqueue with the exception description.
- InMemory: reproduces settlement locally only. It has no cross-process delivery and no durable redelivery.

While an SQS handler runs, visibility is extended once per half visibility timeout so the receipt handle cannot expire mid-handler. Kafka commits offsets through both `AcceptAsync` and `DiscardAsync`; only `ReturnToQueueAsync` seeks without committing. Pair Kafka ingress with inbox idempotency keys and treat handlers as idempotent.

See the broker guides: [AMQP](../integrations/amqp.md), [Kafka](../integrations/kafka.md), [AWS SQS](../integrations/aws-sqs.md), and [Azure Service Bus](../integrations/azure-service-bus.md).

## Poison Messages

| Host | Behavior |
| :--- | :--- |
| Raw `IMessageConsumer` | Handler exceptions requeue indefinitely through `TransportConsumerHandlerInvoker` unless the handler calls `DiscardAsync` or the broker dead-letters after its own redelivery limits. No LiteBus redelivery cap is applied at the transport layer. |
| Inbox ingress | `IngressAckPolicy.ShouldRequeue` requeues transient accept failures when `RequeueOnFailure` is true (the default). Contract, storage, configuration, and poison failures (unregistered contract, `InboxStorageException`, `ArgumentException`, `JsonException`, and similar) discard the delivery. Cancellation never requeues: the channel close requeues the delivery instead. |
| Accept-then-ack failure | When store accept succeeds but broker acknowledgement fails, ingress requeues for redelivery and records `ingress.ack_failed_after_accept`. Stable broker-scoped idempotency absorbs the redelivery into the existing row. |

Use inbox ingress when you need idempotency, store-backed retries, and dead-lettering. Use raw consumers only when the application owns retry caps, poison queues, and idempotent handlers.

## Collect Escalation Data

- LiteBus package versions and the `LiteBusHostManifest` probe list
- One sample stuck row with `message_id`, status, lease fields, and `last_error`
- Processor options with batch size, lease duration, and heartbeat interval
- Recent deploy and migration changes

## Details

| Scenario | Test home |
| :--- | :--- |
| Drain then stop | `LiteBus.Transport.IntegrationTesting` component hosts |
| Lease reclaim | `LiteBus.Storage.IntegrationTests` stress tests |
| NOTIFY reconnect | `PostgreSqlInboxWorkSignalTests` and `PostgreSqlOutboxWorkSignalTests` |
| Ingress ack after accept | `LiteBus.Inbox.UnitTests` |

## Next

- [Reliable Messaging](../reliable-messaging/README.md)
- [Troubleshooting](troubleshooting.md)
- [Management Endpoints](management-endpoints.md)
