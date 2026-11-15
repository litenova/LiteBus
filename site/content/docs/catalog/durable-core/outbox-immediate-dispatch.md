# Outbox Immediate Dispatch

**ID:** `durable-core.outbox-immediate-dispatch`
**Maturity:** GA

## Summary

Outbox immediate dispatch runs the outbox envelopes a mediation just staged in process right after its unit of work commits, before `SendAsync` or `SendManyAsync` returns. A successful dispatch marks the envelope published, so the processor never runs it again; a failed one stays with the processor's normal retry.

## What It Does

`OutboxImmediateDispatchListener` implements `IUnitOfWorkCommitListener` and runs after the commit. It takes the immediate envelope identifiers the mediation recorded in its scoped tracker, claims exactly those rows with `IOutboxTargetedLeaseStore.LeaseByIdAsync`, and passes each leased envelope in staging order through the same envelope handling the outbox processor uses (`OutboxProcessorEnvelopeHandler` and the registered `IOutboxDispatcher`). Immediate envelopes are staged invisible to the processor for `FallbackDelay`, so a crash before dispatch still delivers them after the delay.

## Public Surface

### Consumer Contracts

- **`OutboxModuleBuilder.EnableImmediateDispatch(Action<OutboxImmediateDispatchOptions>?)`**: This is the opt-in on the outbox builder. Without it no envelope is marked for immediate dispatch.
- **`OutboxImmediateDispatchOptions`**: This sealed module-configuration class carries `Timeout` (default 10 seconds), `FallbackDelay` (default 30 seconds, must be greater than `Timeout`), and `Mode`. `Validate()` throws `LiteBusConfigurationException` when the values cannot work together.
- **`OutboxImmediateDispatchMode`**: `Await` (default; the caller waits until the staged envelopes are dispatched or `Timeout` expires) or `Background` (dispatch runs on a background task with its own scope after the commit, and the caller does not wait).
- **`DispatchImmediatelyAttribute`** (`LiteBus.Messaging.Abstractions`): This class-level, non-inherited attribute marks the handler (with per-handler delivery) or the event type (without it) whose envelopes dispatch immediately.
- **`IUnitOfWorkCommitListener`** (`LiteBus.Messaging.Abstractions`): This is the commit notification contract with `OnCommittedAsync`. Command and query unit-of-work completion handlers call every registered listener in registration order after `CommitAsync` returns; a command batch calls them once after its single commit. A listener that throws is logged and does not fail the mediation.
- **`IOutboxTargetedLeaseStore`**: This separate store interface (so third-party `IOutboxLeaseStore` implementations do not break) leases listed envelopes by identifier regardless of `VisibleAfter`.
- **`OutboxTargetedLeaseRequest`**: This sealed record carries `MessageIds`, `LeaseOwner`, `LeaseDuration`, `Now`, and optional `TenantId`.

### Invocation

- **`EnableImmediateDispatch(configure)`**: Call this on the outbox builder with an optional options callback. The options are read once at composition time and validated.
- **`OnCommittedAsync(CancellationToken)`**: The framework calls this on every registered listener after the commit. Applications implement it only for their own post-commit work; immediate dispatch registers its own scoped listener.
- **`LeaseByIdAsync(OutboxTargetedLeaseRequest, CancellationToken)`**: Call this to claim the listed envelopes that are pending and not under an active lease. Envelopes it cannot claim are skipped and not returned.

Typical call sites are the commit path of a command mediation whose read-model projection must be visible when the caller continues, and command batches whose every command staged immediate envelopes in the shared scope.

### Registration

- Call **`EnableImmediateDispatch()`** inside the same outbox module builder that registers storage and dispatch.
- Register a unit of work with **`UseUnitOfWork<TUnitOfWork>()`** on the command module, because dispatch runs after its commit.
- Register exactly one **`IOutboxDispatcher`**: immediate dispatch works with in-process dispatch and with transport dispatchers, which publish to the broker right after the commit.
- Use a store that implements **`IOutboxTargetedLeaseStore`**: the shipped PostgreSQL, Entity Framework Core, and in-memory stores do, as does the test store.
- Call **`EnablePerHandlerDelivery()`** when only some handlers of an event need immediate dispatch; the attribute then goes on the handler class. Without it the attribute goes on the event type.
- Stage through **`ITransactionalOutbox`** (or the generic EF Core variant, or collected domain events drained through them) inside a mediation whose unit of work commits. The plain `IOutbox` writer marks immediate envelopes with fallback-delay visibility but never dispatches them, because it commits without a unit of work.
- The module registers the listener as a scoped **`IUnitOfWorkCommitListener`** and validates the three requirements plus the options through `RegisterCompositionValidation`.

### Configuration

- **`Timeout`** (default 10 seconds, must be positive): The total time immediate dispatch may take after one commit. Envelopes not dispatched in time are released pending with `VisibleAfter` set to now, so the processor takes them at once.
- **`FallbackDelay`** (default 30 seconds, must be greater than `Timeout`): How long an immediate envelope stays invisible to the outbox processor after it is staged. If the process stops before dispatching, the processor delivers the envelope once the delay passes.
- **`Mode`** (default `Await`): Whether the caller waits. In `Await` mode the caller cancellation token is not used once the commit has happened; `Timeout` bounds the wait. In `Background` mode the caller continues at once and the dispatch resolves its services from a fresh scope.
- Processor retry, lease, dead-letter, and hook failure behavior come from **`OutboxProcessorOptions`**, because immediate dispatch reuses the processor's envelope handling. A failed envelope is persisted as failed with the processor's retry visibility.

### Extension Points

- **`IUnitOfWorkCommitListener`**: Implement this scoped contract for application post-commit work beside immediate dispatch. Listeners run in registration order.
- **`IOutboxTargetedLeaseStore`**: Custom stores implement this to support immediate dispatch. It leases atomically with the same side effects as `LeasePendingAsync` (owner, expiry, attempt count, lease generation), bound caller identifier lists on providers with parameter limits.
- **`IProcessorEnvelopeHook`**: The pre- and post-dispatch hooks run around immediate dispatch through the shared envelope handler (see the processor-hooks capability).

## Targeted Leasing

`IOutboxTargetedLeaseStore` exists so immediate dispatch can claim exactly the envelopes one mediation staged, without racing the background processor's due-rows lease. `LeaseByIdAsync` takes `OutboxTargetedLeaseRequest` with the staged message identifiers and claims, atomically, only the listed envelopes that are pending and not under an active lease, regardless of `VisibleAfter`. It sets the owner and expiry, increments the attempt count, and advances the lease generation, exactly like `LeasePendingAsync`.

```csharp
var leased = await leaseStore.LeaseByIdAsync(
    new OutboxTargetedLeaseRequest
    {
        MessageIds = staged,
        LeaseOwner = leaseOwner,
        LeaseDuration = processorOptions.LeaseDuration,
        Now = clock.GetUtcNow(),
        TenantId = processorOptions.TenantId
    },
    cancellationToken).ConfigureAwait(false);
```

Envelopes it cannot claim (already leased, published, dead-lettered, unknown, or outside the tenant filter) are skipped and not returned, so two callers never both receive one envelope. The listener dispatches the leased envelopes in staging order and counts each unclaimed identifier as `skipped`.

The interface stays separate from `IOutboxLeaseStore` so stores written outside LiteBus keep compiling. `PostgreSqlOutboxStore`, `EfCoreOutboxStore` on every supported provider (caller identifier lists sent as one parameter set through `EfCoreCollectionParameters` on SQL Server, which allows 2,100 parameters per command), `InMemoryOutboxStore`, and `TestOutboxStore` implement it. `EnableImmediateDispatch` with a registered store that does not implement it fails at composition with `LiteBusConfigurationException`.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Messaging.Abstractions` | `DispatchImmediatelyAttribute`, `IUnitOfWorkCommitListener` |
| `LiteBus.Outbox.Abstractions` | `IOutboxTargetedLeaseStore`, `OutboxTargetedLeaseRequest` |
| `LiteBus.Outbox` | `EnableImmediateDispatch`, `OutboxImmediateDispatchOptions`, `OutboxImmediateDispatchMode`, listener, tracker, telemetry |
| `LiteBus.Outbox.Storage.*` | Targeted lease implementation (PostgreSQL, Entity Framework Core, in-memory, test) |
| `LiteBus.Outbox.Dispatch.*` | Required dispatcher (in-process or transport) |

## Requires

- Call `EnableImmediateDispatch` inside the outbox builder with `Use*Storage` and exactly one `Use*Dispatch`.
- Register a unit of work (`UseUnitOfWork` on the command module), because dispatch runs after its commit.
- Use a store that implements `IOutboxTargetedLeaseStore`.
- Register the contract for each stored event type.
- Stage immediate envelopes through `ITransactionalOutbox` inside a mediation whose unit of work commits.
- Keep the outbox processor enabled: it is the retry and recovery fallback.

## Invariants

- An envelope is immediate only when its candidate carries `[DispatchImmediately]` (target handler with per-handler delivery, event type otherwise) and its visibility is immediate; scheduled visibility is never immediate.
- Immediate envelopes are staged with `VisibleAfter` set to now plus `FallbackDelay`, using the same clock source as other visibility writes. No schema or EF Core model change was needed.
- A successful immediate dispatch marks the envelope published, so the processor never runs it again. When nothing fails each handler runs once.
- A failed envelope is persisted as failed with the processor's retry visibility, not held for the rest of `FallbackDelay`.
- When `Timeout` expires, every leased envelope not yet dispatched is released pending with `VisibleAfter` set to now.
- The command's outcome never changes because of immediate dispatch: its data is already committed.
- Commit listeners run in registration order after the commit, once per batch; a throwing listener is logged and does not fail the mediation.
- In the worst case (a crash after a handler succeeded and before its envelope was marked published) the envelope is delivered again from the outbox, so immediate handlers stay idempotent.
- `FallbackDelay` must be greater than `Timeout`, and `Timeout` must be positive; composition throws `LiteBusConfigurationException` otherwise.

## Non-Goals

- This capability does not provide exactly-once handler execution: the crash window between handler success and published persist can redeliver.
- This capability does not change the command outcome: post-commit dispatch cannot fail the mediation.
- This capability does not dispatch envelopes staged through the plain `IOutbox` writer, which commits without a unit of work.
- This capability does not replace the outbox processor: failure, timeout, and crash recovery all fall back to it.

## Observability

Immediate dispatch instrumentation reuses meter `LiteBus.Outbox` (`LiteBusOutboxTelemetry.MeterName`). Register with `AddLiteBusOutboxMetrics()` from `LiteBus.Outbox.Extensions.OpenTelemetry`, which needs no change. A failed or timed-out envelope logs a Warning through a `LoggerMessage` method naming the envelope id, contract name, and handler.

### `litebus.outbox.immediate_dispatch.count`

- **Kind**: Counter (`LiteBusOutboxTelemetry.ImmediateDispatchCountInstrumentName`)
- **When emitted**: Once per envelope handled by immediate dispatch
- **Tags/dimensions**: `litebus.outbox.immediate_dispatch.outcome` (`LiteBusOutboxTelemetry.ImmediateDispatchOutcomeAttributeName`) with `published`, `failed`, `skipped`, or `timed_out`
- **How to enable**: `AddLiteBusOutboxMetrics()` subscribes meter `LiteBus.Outbox`
- **Operational note**: The share that is not `published` is the fallback rate. A rising `failed` share points at handler or dispatcher errors; a rising `timed_out` share means `Timeout` is shorter than the dispatch work.

### `litebus.outbox.immediate_dispatch.duration`

- **Kind**: Histogram in seconds (`LiteBusOutboxTelemetry.ImmediateDispatchDurationInstrumentName`)
- **When emitted**: Once per commit, when immediate dispatch finishes (in `Background` mode, when the background dispatch finishes)
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusOutboxMetrics()`
- **Operational note**: Sustained durations near `Timeout` precede `timed_out` outcomes; raise `Timeout` (and `FallbackDelay` with it) or move slow handlers out of the immediate set.

## References

- [Immediate Dispatch](../../reliable-messaging/immediate-dispatch.md)
- [Outbox](../../reliable-messaging/outbox.md)
- [Transactional Messaging Writes](../../reliable-messaging/transactional-writes.md)
- [Domain Events and Unit of Work](../../reliable-messaging/domain-events-and-unit-of-work.md)

## Test Coverage

### Covered Use Cases

#### `ImmediateDispatchCompositionTests.EnableImmediateDispatch_WithoutUnitOfWork_ShouldThrowNamingUnitOfWork`

- **Use case**: Enabling immediate dispatch without a unit of work fails composition
- **Test kind**: Unit
- **Description**: `AddLiteBus` with outbox storage, in-process dispatch, and `EnableImmediateDispatch`, but no unit of work
- **Behavior**: Composition validation for the unit-of-work requirement
- **Expected outcome**: The build throws `LiteBusConfigurationException` naming the unit of work
- **Remarks**: `LiteBus.Outbox.UnitTests` (`ImmediateDispatch/`)

#### `ImmediateDispatchCompositionTests.EnableImmediateDispatch_WithoutDispatcher_ShouldThrowNamingDispatcher`

- **Use case**: Enabling immediate dispatch without a dispatcher fails composition
- **Test kind**: Unit
- **Description**: Unit-of-work stub module with outbox storage and `EnableImmediateDispatch`, but no dispatcher
- **Behavior**: Composition validation for the dispatcher requirement
- **Expected outcome**: The build throws `LiteBusConfigurationException` naming the outbox dispatcher
- **Remarks**: `LiteBus.Outbox.UnitTests` (`ImmediateDispatch/`)

#### `ImmediateDispatchCompositionTests.EnableImmediateDispatch_WithoutTargetedLeaseStore_ShouldThrowNamingTargetedLeaseStore`

- **Use case**: Enabling immediate dispatch with a store lacking targeted leases fails composition
- **Test kind**: Unit
- **Description**: Unit-of-work stub module with a storage module that does not implement `IOutboxTargetedLeaseStore`
- **Behavior**: Composition validation for the targeted lease store requirement
- **Expected outcome**: The build throws `LiteBusConfigurationException` naming `IOutboxTargetedLeaseStore`
- **Remarks**: `LiteBus.Outbox.UnitTests` (`ImmediateDispatch/`)

#### `ImmediateDispatchCompositionTests.EnableImmediateDispatch_FallbackDelayShorterThanTimeout_ShouldThrow`

- **Use case**: A fallback delay shorter than the timeout fails composition
- **Test kind**: Unit
- **Description**: `EnableImmediateDispatch` with a 30-second timeout and a 10-second fallback delay
- **Behavior**: `OutboxImmediateDispatchOptions.Validate`
- **Expected outcome**: The build throws `LiteBusConfigurationException` naming the `FallbackDelay` and `Timeout` rule
- **Remarks**: `LiteBus.Outbox.UnitTests` (`ImmediateDispatch/`)

#### `ImmediateDispatchCompositionTests.EnableImmediateDispatch_FallbackDelayEqualToTimeout_ShouldThrow`

- **Use case**: A fallback delay equal to the timeout fails composition
- **Test kind**: Unit
- **Description**: `EnableImmediateDispatch` with a 10-second timeout and a 10-second fallback delay
- **Behavior**: `OutboxImmediateDispatchOptions.Validate`
- **Expected outcome**: The build throws `LiteBusConfigurationException` naming the `FallbackDelay` and `Timeout` rule
- **Remarks**: `LiteBus.Outbox.UnitTests` (`ImmediateDispatch/`)

#### `ImmediateDispatchCompositionTests.EnableImmediateDispatch_WithAllRequirements_ShouldSucceed`

- **Use case**: Enabling immediate dispatch with every requirement succeeds
- **Test kind**: Unit
- **Description**: Unit-of-work stub module with in-memory storage, in-process dispatch, and `EnableImmediateDispatch`
- **Behavior**: Full composition with no option overrides
- **Expected outcome**: The build succeeds without throwing
- **Remarks**: `LiteBus.Outbox.UnitTests` (`ImmediateDispatch/`)

#### `PostgreSqlOutboxImmediateDispatchEndToEndTests.SendAsync_WhenImmediateHandlerSucceeds_ShouldMakeProjectionReadableOnReturn`

- **Use case**: A successful immediate dispatch makes the projection readable when `SendAsync` returns
- **Test kind**: Integration
- **Description**: PostgreSQL outbox with per-handler delivery and `EnableImmediateDispatch`, composed through `AddLiteBus`
- **Behavior**: `SendAsync` of a command that stages an immediate envelope
- **Expected outcome**: The projection row exists on return, the envelope is `Published` with attempt count 1, and the projection handler ran once
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

#### `PostgreSqlOutboxImmediateDispatchEndToEndTests.SendAsync_WhenHealthyHandlersRun_ShouldRunEachHandlerExactlyOnce`

- **Use case**: Healthy handlers run exactly once across repeated commands
- **Test kind**: Integration
- **Description**: Five sequential commands against PostgreSQL with the processor polling
- **Behavior**: `SendAsync` per command with immediate and audit handlers
- **Expected outcome**: Every handler run is counted once per command in the handler runs table, and every row reaches `Published`
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

#### `PostgreSqlOutboxImmediateDispatchEndToEndTests.SendAsync_WhenImmediateHandlerFails_ShouldSucceedCommandAndPublishLaterThroughProcessor`

- **Use case**: A failing immediate handler keeps the command succeeded and publishes later through the processor
- **Test kind**: Integration
- **Description**: A handler that fails its first run and succeeds its second, against PostgreSQL
- **Behavior**: `SendAsync` followed by processor retry
- **Expected outcome**: The command returns its result, the envelope is `Failed` right after the call, then the processor publishes it and the handler succeeds once
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

#### `PostgreSqlOutboxImmediateDispatchEndToEndTests.SendAsync_WhenCommitListenerThrowsBeforeImmediateDispatch_ShouldDeliverThroughProcessorAfterFallbackDelay`

- **Use case**: A commit listener failure before immediate dispatch still delivers through the processor after the fallback delay
- **Test kind**: Integration
- **Description**: A throwing listener registered ahead of immediate dispatch, simulating a crash between staging and dispatch
- **Behavior**: `SendAsync` with `FallbackDelay` of 5 seconds, then processor polling
- **Expected outcome**: The command succeeds, the immediate envelope stays pending and invisible during the delay with zero handler runs, then the processor publishes it exactly once
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

#### `PostgreSqlOutboxImmediateDispatchEndToEndTests.TwoHosts_WhenSendingConcurrently_ShouldDispatchEveryEnvelopeExactlyOnce`

- **Use case**: Two hosts sending concurrently dispatch every envelope exactly once
- **Test kind**: Integration
- **Description**: Two providers with distinct lease owners sharing one PostgreSQL outbox table, each sending 10 commands
- **Behavior**: Concurrent `SendAsync` with immediate dispatch and both processors polling
- **Expected outcome**: Every envelope reaches `Published` and every handler ran once per envelope
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

#### `PostgreSqlOutboxImmediateDispatchEndToEndTests.SendManyAsync_WhenBatchCommits_ShouldDispatchEveryCommandEnvelopeBeforeReturn`

- **Use case**: A command batch dispatches every command's envelopes before `SendManyAsync` returns
- **Test kind**: Integration
- **Description**: A batch of 3 commands sharing one mediation scope against PostgreSQL
- **Behavior**: `SendManyAsync` with a commit-counting listener registered
- **Expected outcome**: Commit listeners run once, every projection is readable on return, and every immediate envelope is `Published` with attempt count 1
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

#### `PostgreSqlOutboxImmediateDispatchEndToEndTests.SendManyAsync_WhenImmediateDispatchTimesOut_ShouldReleaseRemainingEnvelopesForProcessor`

- **Use case**: A timed-out immediate dispatch releases its remaining envelopes for the processor at once
- **Test kind**: Integration
- **Description**: A batch whose handlers block on a gate, with a 2-second `Timeout`
- **Behavior**: `SendManyAsync` past the timeout, then the gate opens and the processor polls
- **Expected outcome**: On return the envelopes are `Pending` with `VisibleAfter` set to the send window and zero runs; after the gate opens the processor publishes each exactly once
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

#### `EfCoreOutboxImmediateDispatchEndToEndTests.SendAsync_ShouldProjectImmediatelyAndRunEachHandlerOnce`

- **Use case**: An EF Core command projects immediately and runs each handler once
- **Test kind**: Integration
- **Description**: Entity Framework Core outbox storage on PostgreSQL with per-handler delivery and `EnableImmediateDispatch`
- **Behavior**: `SendAsync` of a command that stages an immediate envelope
- **Expected outcome**: The domain row and the projection are readable on return, the envelope is `Published` with attempt count 1, and both handlers ran once
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`EntityFrameworkCore/Outbox/`)

#### `EfCoreOutboxImmediateDispatchEndToEndTests.SendAsync_WhenImmediateHandlerFails_ShouldSucceedAndLetProcessorPublishLater`

- **Use case**: A failing immediate handler on EF Core keeps the command succeeded and publishes later through the processor
- **Test kind**: Integration
- **Description**: A handler that fails its first run and succeeds its second, with EF Core storage
- **Behavior**: `SendAsync` followed by processor retry
- **Expected outcome**: The command returns its receipt, the envelope is `Failed` right after the call, then the processor publishes it and each handler succeeds
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`EntityFrameworkCore/Outbox/`)

#### `EfCoreOutboxImmediateDispatchEndToEndTests.SendAsync_WhenCommitListenerBeforeImmediateDispatchThrows_ShouldStillSucceedAndDeliverOnce`

- **Use case**: A throwing commit listener ahead of immediate dispatch still succeeds and delivers once on EF Core
- **Test kind**: Integration
- **Description**: A listener registered before immediate dispatch that throws, simulating a crash between staging and dispatch
- **Behavior**: `SendAsync` with the processor polling
- **Expected outcome**: The command succeeds and the envelope is delivered exactly once through the fallback
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`EntityFrameworkCore/Outbox/`)

#### `EfCoreOutboxImmediateDispatchEndToEndTests.EnqueueAsync_WhenImmediateDispatchNeverStarted_ShouldBeDeliveredByProcessorAfterFallbackDelay`

- **Use case**: An immediate envelope with no commit notification is delivered by the processor after the fallback delay
- **Test kind**: Integration
- **Description**: A plain `IOutbox` enqueue (no unit-of-work commit) with a 3-second `FallbackDelay`
- **Behavior**: Enqueue followed by processor polling
- **Expected outcome**: The row stays `Pending` and invisible during the delay with zero handler runs, then the processor publishes it with attempt count 1
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`EntityFrameworkCore/Outbox/`)

#### `EfCoreOutboxImmediateDispatchEndToEndTests.SendAsync_WithTwoHostsSharingOneTable_ShouldDispatchEachEnvelopeOnce`

- **Use case**: Two EF Core hosts sharing one table dispatch each envelope once
- **Test kind**: Integration
- **Description**: Two providers with distinct lease owners on one EF Core outbox table, one sending 5 commands while both processors poll
- **Behavior**: `SendAsync` per command with immediate dispatch on both hosts
- **Expected outcome**: All 5 rows reach `Published` with attempt count 1, and each handler ran once per order
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`EntityFrameworkCore/Outbox/`)

#### `EfCoreOutboxImmediateDispatchEndToEndTests.SendManyAsync_ShouldTriggerOneImmediateDispatchCoveringEveryCommand`

- **Use case**: An EF Core command batch triggers one immediate dispatch covering every command
- **Test kind**: Integration
- **Description**: A batch sharing one mediation scope with a commit-counting listener
- **Behavior**: `SendManyAsync` with immediate envelopes from every command
- **Expected outcome**: Commit listeners run once, both projections are readable on return, and every envelope is delivered once per handler
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`EntityFrameworkCore/Outbox/`)

#### `EfCoreOutboxImmediateDispatchEndToEndTests.SendAsync_WhenTimeoutExpires_ShouldReleaseRemainingEnvelopesForProcessor`

- **Use case**: An expired timeout on EF Core releases the remaining envelopes for the processor
- **Test kind**: Integration
- **Description**: A send whose handlers block past a short `Timeout`
- **Behavior**: `SendAsync` past the timeout, then processor polling
- **Expected outcome**: On return the envelopes are `Pending` with `VisibleAfter` set to the send window; the processor then publishes each exactly once
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`EntityFrameworkCore/Outbox/`)

#### `ImmediateDispatchHttpAcceptanceTests.Post_WhenImmediateDispatchSucceeds_GetReturnsProjectionImmediately`

- **Use case**: A GET issued right after the command POST returns the immediately dispatched projection
- **Test kind**: Integration
- **Description**: HTTP acceptance through `TestHost` against PostgreSQL, with the projection read through a GET endpoint
- **Behavior**: POST a command, then GET the projection
- **Expected outcome**: The GET returns the projected status without waiting for a processor poll
- **Remarks**: `LiteBus.Extensions.IntegrationTests` (`AspNetCore/`)

#### `ImmediateDispatchHttpAcceptanceTests.Post_WhenImmediateDispatchFails_GetReturnsProjectionAfterProcessorRetry`

- **Use case**: A failed immediate dispatch still serves the projection after processor retry
- **Test kind**: Integration
- **Description**: HTTP acceptance through `TestHost` against PostgreSQL with a dispatch that fails first
- **Behavior**: POST a command whose immediate dispatch fails, then GET the projection after retry
- **Expected outcome**: The POST succeeds and the GET returns the projected status once the processor retry delivers the envelope
- **Remarks**: `LiteBus.Extensions.IntegrationTests` (`AspNetCore/`)

#### `OutboxStoreContractTests.LeaseByIdAsync_WhenRequestListsPendingRows_ShouldLeaseOnlyListedRows`

- **Use case**: A targeted lease claims only the listed pending rows
- **Test kind**: Contract
- **Description**: Three pending rows with two identifiers in the request, in the shared contract suite across backends
- **Behavior**: `LeaseByIdAsync` with a subset of stored identifiers
- **Expected outcome**: The two listed envelopes return as `Publishing` with owner, expiry, and attempt count 1, and the unlisted row stays `Pending`
- **Remarks**: Shared contract suite

#### `OutboxStoreContractTests.LeaseByIdAsync_WhenRowsAreLeasedPublishedOrDeadLettered_ShouldSkipUnclaimableRows`

- **Use case**: A targeted lease skips actively leased, published, dead-lettered, and unknown rows
- **Test kind**: Contract
- **Description**: One row per state plus an unknown identifier in one request
- **Behavior**: `LeaseByIdAsync` across mixed row states
- **Expected outcome**: Only the pending row returns; the leased row keeps its original owner
- **Remarks**: Shared contract suite

#### `OutboxStoreContractTests.LeaseByIdAsync_WhenVisibleAfterIsInFuture_ShouldLeaseRow`

- **Use case**: A targeted lease claims a pending row even when its visibility timestamp is in the future
- **Test kind**: Contract
- **Description**: A row with `VisibleAfter` two hours out, leased 30 minutes in
- **Behavior**: `LeaseByIdAsync` ignoring `VisibleAfter`
- **Expected outcome**: The row returns as `Publishing`
- **Remarks**: Shared contract suite

#### `OutboxStoreContractTests.LeaseByIdAsync_ShouldIncrementAttemptCountAndLeaseGeneration`

- **Use case**: A targeted lease records owner, expiry, attempt count, and lease generation
- **Test kind**: Contract
- **Description**: Two successive targeted leases of one row with a failed persist between them
- **Behavior**: `LeaseByIdAsync`, `PersistAsync` as failed, then `LeaseByIdAsync` again
- **Expected outcome**: The second lease shows attempt count 2, an incremented lease generation, and the new owner
- **Remarks**: Shared contract suite

#### `OutboxStoreContractTests.LeaseByIdAsync_ConcurrentCallers_ShouldLeaseDisjointMessages`

- **Use case**: Two concurrent targeted leases claim disjoint message sets
- **Test kind**: Contract
- **Description**: Two simultaneous `LeaseByIdAsync` calls for one row with different owners
- **Behavior**: Concurrent targeted leasing
- **Expected outcome**: Exactly one caller receives the envelope
- **Remarks**: Shared contract suite

#### `OutboxStoreContractTests.LeaseByIdAsync_WithMoreIdentifiersThanSqlServerParameterLimit_ShouldLeaseMatchingRows`

- **Use case**: A targeted lease with more identifiers than the SQL Server parameter limit still claims the stored rows
- **Test kind**: Contract
- **Description**: A request with 2,502 identifiers (2 stored, 2,500 unknown) against every backend
- **Behavior**: `LeaseByIdAsync` with a caller-sized identifier list past the 2,100-parameter limit
- **Expected outcome**: The two stored envelopes return
- **Remarks**: Shared contract suite; EF Core sends the list as one parameter set through `EfCoreCollectionParameters` on SQL Server

#### `OutboxStoreContractTests.LeaseByIdAsync_WhenTenantFilterDoesNotMatchStoredTenant_ShouldNotLeaseRow`

- **Use case**: A targeted lease with a non-matching tenant filter does not claim the row
- **Test kind**: Contract
- **Description**: A row stored for one tenant, leased with another tenant filter
- **Behavior**: `LeaseByIdAsync` with `TenantId` set
- **Expected outcome**: No envelope returns
- **Remarks**: Shared contract suite

#### `OutboxStoreContractTests.LeaseByIdAsync_WhenTenantFilterMatchesStoredTenant_ShouldLeaseRow`

- **Use case**: A targeted lease with a matching tenant filter claims the row
- **Test kind**: Contract
- **Description**: A row stored for one tenant, leased with the same tenant filter
- **Behavior**: `LeaseByIdAsync` with `TenantId` set
- **Expected outcome**: The envelope returns
- **Remarks**: Shared contract suite

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Immediate dispatch with `Background` mode under load | Yes | No test measures background dispatch lag or scope disposal under sustained commands | Integration | Medium |
| Immediate dispatch with a transport dispatcher to a real broker | Yes | End-to-end tests use in-process dispatch; broker publish right after commit is unproven | Integration | Medium |
| Immediate dispatch telemetry counters and duration | Yes | No test asserts the count outcomes or the duration record per commit | Integration | Low |

### Out-of-Scope Use Cases

- Exactly-once handler execution across the crash window
- Command outcome changes from post-commit dispatch
- Immediate dispatch of envelopes staged through the plain `IOutbox` writer
- Processor replacement: failure, timeout, and crash recovery always fall back to the processor
