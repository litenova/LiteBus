# PostgreSQL Work Signal

- **ID**: `storage.postgresql.work-signal`
- **Summary**: Processor wake-up mechanism based on PostgreSQL `LISTEN` and `NOTIFY`, with poll fallback.

## Interface and Methods

| Type | Methods |
| --- | --- |
| `PostgreSqlWorkSignal` | `WaitForWorkOrDelayAsync`, `DisposeAsync` |
| Inbox and outbox PostgreSQL stores | append paths trigger `NOTIFY` through table insert trigger |

## SQL and Trigger Behavior

- Inbox and outbox v1 create scripts define:
  - per-table notify function (`insert_notify_fn`)
  - after-insert trigger (`insert_notify_trg`)
- Trigger payload is empty; event is wake-only and does not carry message data.
- If notifications are missed, processors still run on the configured poll interval.

## Concurrency and Reliability

- A dedicated listener connection is opened for each work signal instance.
- Listener loop catches Npgsql and generic runtime failures, invalidates connection, then reconnects.
- Reconnect backoff is one second in `PostgreSqlWorkSignal`.
- Notification delivery improves wake latency but does not change correctness rules.

## Index Interaction

- No dedicated index is tied to work signal.
- Processor claim performance still depends on lease index on each store table.

## Observability

- No dedicated work-signal meter exists.
- Observable effect appears in processor and queue-depth metrics:
  - `litebus.inbox.processor.state`
  - `litebus.outbox.processor.state`
  - `litebus.inbox.processor.leases_acquired`
  - `litebus.outbox.processor.leases_acquired`
- Queue-depth gauges update after diagnostics scans, not at notification time.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxWorkSignalNotifyTests` and `PostgreSqlOutboxWorkSignalNotifyTests`: wake before poll timeout on row insert.
- `PostgreSqlInboxWorkSignalTests` and `PostgreSqlOutboxWorkSignalTests`: listener reconnection after forced break.
- `PostgreSqlInboxHostingIntegrationTests`: end-to-end host wake behavior through processor background service.

### `LiteBus.Storage.UnitTests`

- Work signal is mostly covered through integration tests; shared retry/connection behavior is indirectly exercised by PostgreSQL utility tests.

## Related Docs

- [Architecture](../../architecture/README.md#storage-axis)
- [Reliable Messaging](../../reliable-messaging/README.md)
