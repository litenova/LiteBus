# PostgreSQL Work Signal

- **ID**: `storage.postgresql.work-signal`
- **Summary**: Processor wake-up mechanism built on PostgreSQL `LISTEN` and `NOTIFY`, with polling as a fallback.

## Interface and Methods

| Type | Methods |
| --- | --- |
| `PostgreSqlWorkSignal` | `WaitForWorkOrDelayAsync`, `DisposeAsync` |
| Inbox and outbox PostgreSQL stores | append paths trigger `NOTIFY` through table insert trigger |

## SQL and Trigger Behavior

- Inbox and outbox v1 create scripts define:
  - per-table notify function (`insert_notify_fn`)
  - after-insert trigger (`insert_notify_trg`)
- The trigger payload is empty. The event only wakes the processor and carries no message data.
- If notifications are missed, processors still run on the configured poll interval.

## Concurrency and Reliability

- Each work signal instance opens a dedicated listener connection.
- `IProcessorWorkSignal` extends `IAsyncDisposable`, and built-in signals also implement `IDisposable`. PostgreSQL LISTEN work signals register through singleton factories so the container disposes them, closing the dedicated LISTEN connection.
- The listener loop catches Npgsql exceptions and other runtime failures, invalidates the connection, then reconnects.
- Reconnect backoff is one second in `PostgreSqlWorkSignal`.
- Notification delivery improves wake latency but does not change correctness rules.

## Index Interaction

- No dedicated index serves the work signal.
- Processor claim performance still depends on the lease index on each store table.

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

- `PostgreSqlInboxWorkSignalNotifyTests` and `PostgreSqlOutboxWorkSignalNotifyTests` verify wake-up before the poll timeout on row insert.
- `PostgreSqlInboxWorkSignalTests` and `PostgreSqlOutboxWorkSignalTests` verify listener reconnection after a forced break.
- `PostgreSqlInboxHostingIntegrationTests` covers end-to-end host wake behavior through the processor background service.

### `LiteBus.Storage.UnitTests`

- Integration tests cover most of the work signal. PostgreSQL utility tests indirectly exercise the shared retry and connection behavior.

## Related Docs

- [Architecture](../../architecture/README.md#storage-axis)
- [Reliable Messaging](../../reliable-messaging/README.md)
