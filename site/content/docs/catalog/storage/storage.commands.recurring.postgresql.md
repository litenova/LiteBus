# Commands Recurring PostgreSQL Adapter

- **ID**: `storage.commands.recurring.postgresql`
- **Package**: `LiteBus.Commands.Recurring.Storage.PostgreSql` (feature bridge)
- **Shared dependency**: `LiteBus.Storage.PostgreSql` (technology adapter)
- **Summary**: A singleton `PostgreSqlRecurringCommandLease` implements recurring command single-flight leases as renewable rows in `litebus_recurring_leases`. The command module registers it as a child module.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `IRecurringCommandLease` | `TryAcquireAsync` returning `IRecurringCommandLeaseHandle` with `LeaseLost` |

## SQL Behavior

- The table is configurable and defaults to `public.litebus_recurring_leases`, with columns `lease_name`, `holder`, `acquired_at`, `expires_at`, and `renewed_at`.
- Registration uses `commands.UsePostgreSqlRecurringLease(postgres => { postgres.UseConnectionString(...); postgres.UseOptions(new PostgreSqlRecurringLeaseOptions { LeaseDuration = ... }); })`, composed inside `AddCommands`. Loops opt in per registration with `RecurringCommandHostOptions.RequireLease`.
- Each acquisition is one upsert. The row is inserted when absent, or taken over when its `expires_at` has passed on the database clock. The statement returns a row only to the winner, so a racing holder gets `null` and the loop skips the pass.
- Every timestamp comes from `now()`. Host clocks are never read, so skew between replicas cannot shorten or extend a lease.
- The holder identity is the machine name plus the process id plus a per-instance GUID, recorded on every row so operators can see which replica holds each lease.
- The returned handle renews the row in the background every `RenewInterval` (one third of `LeaseDuration` by default) and deletes it on dispose, but only while this holder still owns it. The renewal revives an expired row nobody took and leaves alone a row another holder took. A renewal that finds the lease taken or fails cancels the handle's `LeaseLost` token.
- `LeaseDuration` defaults to 30 seconds. Both values are validated positive when the module builds, and an explicit `RenewInterval` at or above `LeaseDuration` is rejected.
- Each statement opens its own pooled connection and no connection is held between statements, so the lease works behind PgBouncer in transaction mode and with pooling disabled.
- `DisableSchemaInitialization()` leaves table creation to a migration pipeline. Acquisition fails until the table exists.

## Concurrency Model

- Claim atomicity comes from the `lease_name` primary key plus the conditional upsert. Two concurrent acquisitions of one name grant exactly one.
- A holder whose process dies stops renewing, so its row becomes takeable once the lease duration lapses on the database clock. No connection state is involved.
- A renewal that finds the row taken or gone stops the loop, cancels the handle's `LeaseLost` token, and logs once instead of retrying against the store. The loop holding the lease ends its busy period and backs off.

## Indexes

Schema v1 creates and validates:

- The primary key is `lease_name`.

## Observability

- The schema probe `recurring.postgresql.schema` validates columns and indexes.
- Renewal events are `2110` `LeaseRenewalStolen`, logged at Warning when the lease was taken by another holder, and `2111` `LeaseRenewalFailed`, logged at Warning when renewal failed and the lease expires on the database clock.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlRecurringCommandLeaseTests` covers held names returning null until released, distinct names granting both, racing holders granting exactly one, takeover after expiry without renewal, same-holder re-acquire refused, renew-interval validation, owner disposal through the schema initializer, lease-loss signalling on steal and on renewal failure, renewal holding past original expiry, stolen and dropped-table renewals stopping without resurrecting the row, pooled and unpooled sources coordinating, idempotent schema creation, drift on a missing table, the schema probe healthy and unhealthy, and schema creation through the module initializer.

## Related Docs

- [Recurring Commands](../mediator/recurring-commands.md)
- [Recurring commands guide](../../integrations/recurring-commands.md)
- [PostgreSQL Schema Management](../../integrations/postgresql-schema-management.md)
