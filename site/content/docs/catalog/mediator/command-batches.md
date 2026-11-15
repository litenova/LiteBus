# Command Batches

- **ID**: `mediator.command-batches`
- **Name**: Command batches
- **Maturity**: GA
- **Summary**: Sends several commands in list order through one dispatch scope, committing the unit of work once after all of them, or rolling back on the first failure.
- **Guide**: [Command module](../../concepts/commands.md)

## What It Does

`ICommandBatchMediator` sends a list of commands as one atomic batch. The batch creates one `IMessageDispatchScope` and runs every command in list order through its full pipeline: guards, validators, shortcuts, pre-handlers, the main handler, post-handlers, error handlers, and completion handlers. Commands never run in parallel, because a scoped `DbContext` is not thread-safe. All commands resolve handlers, the unit of work, the outbox, and the collector from the one scope, so they share the scoped unit of work, the transactional outbox, and the domain event collector. Batches are a separate interface rather than new members on `ICommandMediator`, which has implementers outside `LiteBus.Commands`, so adding members would break them. No builder method enables batches: `CommandModule.Build()` registers `ICommandBatchMediator` with the same lifetime as `ICommandMediator`.

The per-command unit-of-work completion handler does not commit and does not roll back inside a batch. The batch calls `IUnitOfWorkBoundary.BeginAsync` once before the first command when the registered unit of work implements the boundary hook, `IUnitOfWork.CommitAsync` once after every command passed, and `RollbackAsync` when the batch stops or the commit throws. A command counts as passed when its outcome is `Succeeded` or `Answered`: a shortcut answer, including an idempotent repeat, did nothing and is not a failure. The first command whose outcome is `Denied`, `Invalid`, a fault, or a cancellation stops the batch, and no later command runs. When no unit of work is registered, a batch still runs in one scope and stops on the first failure, but it has nothing to commit; atomicity then depends entirely on the application.

`TrySendManyAsync` returns the `CommandBatchResult` for refusals, while a fault, a cancellation, or a failed commit propagates as an exception after the rollback steps have run, matching how `TrySendAsync` lets faults propagate. `SendManyAsync` throws for every non-committed ending: it rethrows the refusal exception the failing command raised, the same exception `SendAsync` would raise, or the fault, or the commit exception. The cancellation token is checked before each command; a cancellation between or during commands runs the rollback steps and throws `OperationCanceledException` from both methods, while the commit step uses `CancellationToken.None`, as the single-command commit does. Commands that implement `ICommand<TResult>` are accepted and run, but their values are not returned. Each command's staged domain events land in the shared scope's `ITransactionalOutbox`, so they commit or roll back with the batch.

Idempotency and auditing settle once for the whole batch rather than per command. Inside a batch, the idempotency completion handler never settles per command, for any store; on commit, `CompleteAsync` for every pending claim runs before `CommitAsync` for an unmarked store and after it for a marked store, with the same retry and logging rule as single commands, and on rollback `ReleaseAsync` runs for every pending claim. Two commands in one batch that resolve to the same scoped idempotency key stop the batch: the second is refused with `litebus.batch.duplicate_idempotency_key` (`IdempotencyRefusalCodes.DuplicateKeyInBatch`), detected from the batch's pending claims before the store is asked. See [Command idempotency](command-idempotency.md). The audit writer builds each command's record as usual but buffers it with the `litebus.batch.id` property instead of calling `IAuditTrail`; on commit the buffer is written in order before `CommitAsync`, and on rollback each buffered record that reported success is rewritten to `Failed` with `litebus.batch.rolled_back` while the stopping command keeps its own outcome, then written after `RollbackAsync`. A trail that writes into the rolled-back transaction loses these records, which matches what happens to a failed single command. See [Auditing](auditing.md).

A batch cannot start inside another batch. `SendManyAsync` or `TrySendManyAsync` called from a handler running inside a batch throws `InvalidOperationException`, because each batch commits on its own and a nested batch would commit independently of the batch that contains it. A plain `SendAsync` from such a handler is not refused at runtime; it opens its own scope and commits on its own, as nested mediation does today, and `LB1023` reports it at build time. See [Nested mediation in command handler](../analyzers/nested-mediation-in-command-handler.md).

A large batch holds its locks for its whole run, so split work into smaller batches in the calling code rather than sending thousands of commands at once. LiteBus never splits a batch, because that would break atomicity. A source that produces one command per item on an interval uses recurring batches instead of one ever-growing batch. See [Recurring commands](recurring-commands.md).

## Public Surface

A scheduled job that settles one business day with three commands, failing the job when the batch does not commit:

```csharp
public sealed class NightlySettlementJob
{
    private readonly ICommandBatchMediator _batches;

    public NightlySettlementJob(ICommandBatchMediator batches)
    {
        _batches = batches;
    }

    public Task SettleAsync(DateOnly businessDay, CancellationToken cancellationToken)
    {
        IReadOnlyList<ICommand> commands =
        [
            new CloseBusinessDayCommand(businessDay),
            new AccrueFeesCommand(businessDay),
            new NotifyMerchantsCommand(businessDay),
        ];

        return _batches.SendManyAsync(commands, cancellationToken: cancellationToken);
    }
}
```

A scheduled job that treats a refusal as a report rather than a fault, logging the `CommandBatchResult` from `TrySendManyAsync`:

```csharp
public sealed class NightlySettlementReporter
{
    private readonly ICommandBatchMediator _batches;
    private readonly ILogger<NightlySettlementReporter> _logger;

    public NightlySettlementReporter(ICommandBatchMediator batches, ILogger<NightlySettlementReporter> logger)
    {
        _batches = batches;
        _logger = logger;
    }

    public async Task SettleAsync(DateOnly businessDay, CancellationToken cancellationToken)
    {
        IReadOnlyList<ICommand> commands =
        [
            new CloseBusinessDayCommand(businessDay),
            new AccrueFeesCommand(businessDay),
            new NotifyMerchantsCommand(businessDay),
        ];

        var result = await _batches.TrySendManyAsync(commands, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Settlement batch {Outcome} with {Count} entries; failed index: {FailedIndex}.",
            result.Outcome,
            result.Entries.Count,
            result.FailedIndex);
    }
}
```

| API | Role |
| --- | --- |
| `ICommandBatchMediator.SendManyAsync(IReadOnlyList<ICommand>, CommandBatchSettings?, CancellationToken)` | Sends the batch and throws the refusal, fault, or commit exception unless it committed |
| `ICommandBatchMediator.TrySendManyAsync(IReadOnlyList<ICommand>, CommandBatchSettings?, CancellationToken)` | Sends the batch and returns refusals as a `CommandBatchResult`; faults, cancellations, and commit failures propagate |
| `CommandBatchSettings` | `Mediation` applied to every command; each command gets its own copy of `Items`, so one command's pipeline cannot see another's |
| `CommandBatchResult` | `Outcome`, `Entries` in input order, and `FailedIndex` (the index that stopped the batch, or `null`) |
| `CommandBatchOutcome` | `Committed` or `RolledBack` |
| `CommandBatchEntryResult` | `Index`, `CommandType`, `Executed`, and `Result` (`null` when the command did not run) |
| `IdempotencyRefusalCodes.DuplicateKeyInBatch` | `litebus.batch.duplicate_idempotency_key`, when two commands in one batch resolve to the same scoped key |
| `TestCommandBatchMediator` | Recording test double from `LiteBus.Testing.Mediation`, with a configurable result and a refusal at an index |

## Packages

- `LiteBus.Commands.Abstractions` (`ICommandBatchMediator`, `CommandBatchSettings`, `CommandBatchResult`, `CommandBatchOutcome`, `CommandBatchEntryResult`)
- `LiteBus.Commands` (`CommandBatchMediator`, registered with the same lifetime as `ICommandMediator`)
- `LiteBus.Messaging.Abstractions` (`IUnitOfWorkBoundary`, `IdempotencyRefusalCodes`)
- `LiteBus.Messaging` (batch telemetry names on `LiteBusMediationTelemetry`)
- `LiteBus.Testing.Mediation` (`TestCommandBatchMediator`)

## Requires

- `mediator.commands`
- `mediator.handler-pipeline`
- `mediator.unit-of-work`

## Invariants

- One batch creates one `IMessageDispatchScope`, and every command resolves handlers, the unit of work, the outbox, and the collector from it.
- Commands run one at a time in list order, each through its full pipeline, and never in parallel.
- The per-command unit-of-work completion handler neither commits nor rolls back inside a batch.
- `BeginAsync` runs once before the first command when the registered unit of work implements `IUnitOfWorkBoundary`; `CommitAsync` runs once after every command passed; `RollbackAsync` runs when the batch stops or the commit throws.
- A command counts as passed when its outcome is `Succeeded` or `Answered`. The first command whose outcome is `Denied`, `Invalid`, a fault, or a cancellation stops the batch, and no later command runs.
- `TrySendManyAsync` returns `RolledBack` with `FailedIndex` for a refusal stop. A fault, a cancellation, or a failed commit propagates as an exception after the rollback steps have run. `SendManyAsync` rethrows the refusal exception the failing command raised, or the fault, or the commit exception.
- The token is checked before each command, and a cancellation runs the rollback steps and throws `OperationCanceledException` from both methods. The commit step uses `CancellationToken.None`.
- Commands after the failing one are reported with `Executed = false` and a `null` result.
- An empty list returns `Committed` with no entries, opens no scope, and calls no unit-of-work method.
- A `null` list throws `ArgumentNullException`; a `null` element throws `ArgumentException` naming the index.
- A batch started from inside another batch throws `InvalidOperationException`. A plain `SendAsync` from a handler inside a batch opens its own scope and commits on its own.
- Without a registered unit of work, a batch still stops on the first failure, but atomicity depends entirely on the application.
- Commands implementing `ICommand<TResult>` run with their values dropped.
- Inside a batch, no command settles idempotency per command. On commit, `CompleteAsync` for every pending claim runs before `CommitAsync` for an unmarked store and after it for a marked store; on rollback, `ReleaseAsync` runs for every pending claim.
- Two commands in one batch resolving to the same scoped idempotency key stop the batch with `litebus.batch.duplicate_idempotency_key`, detected from the batch's pending claims before the store is asked.
- Every buffered audit record carries the `litebus.batch.id` property. On commit the buffer is written in order before `CommitAsync`. On rollback, records that reported success are rewritten to `Failed` with `litebus.batch.rolled_back`, the stopping command keeps its own outcome, commands that never ran get no record, and the rewritten buffer is written after `RollbackAsync`.
- Staged domain events from every command land in the one shared outbox and are discarded on rollback.

## Non-Goals

- Dependent sequences that pass one command's result into the next. Generate identifiers in the caller, or react to events.
- Batching events or queries, and the inbox processor committing several messages in one transaction.
- Distributed transactions.
- Splitting a large batch. The caller splits work into smaller batches; LiteBus never splits one, because that would break atomicity.

## Observability

Batch telemetry lives on the existing `LiteBus.Mediation` meter and activity source, so no OpenTelemetry package change is needed. One activity spans each batch, and each command's own mediation activity is its child.

| Name | Constant | Kind |
| --- | --- | --- |
| `litebus.mediation.batch` | `LiteBusMediationTelemetry.BatchActivityName` | Activity per batch |
| `litebus.mediation.batch.duration` | `LiteBusMediationTelemetry.BatchDurationInstrumentName` | Histogram in seconds, tagged with `litebus.batch.outcome` (`committed` or `rolled_back`) |
| `litebus.mediation.batch.size` | `LiteBusMediationTelemetry.BatchSizeInstrumentName` | Histogram of the number of commands in the batch |
| `litebus.batch.outcome` | `LiteBusMediationTelemetry.BatchOutcomeAttributeName` | Outcome tag and activity attribute |

These names are public contract: treat a rename or a removal as a breaking change. They are recorded only when telemetry is enabled, gated in the same way as the existing duration instrument.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `SendManyAsync_NullCommands_ShouldThrowArgumentNullException` | `LiteBus.Mediator.UnitTests` |
| `TrySendManyAsync_NullCommands_ShouldThrowArgumentNullException` | `LiteBus.Mediator.UnitTests` |
| `SendManyAsync_NullElement_ShouldThrowArgumentExceptionNamingIndex` | `LiteBus.Mediator.UnitTests` |
| `TrySendManyAsync_NullElement_ShouldThrowArgumentExceptionNamingIndex` | `LiteBus.Mediator.UnitTests` |
| `TrySendManyAsync_EmptyList_ShouldReturnCommittedWithoutEntries` | `LiteBus.Mediator.UnitTests` |
| `SendManyAsync_EmptyList_ShouldCompleteWithoutUnitOfWorkCalls` | `LiteBus.Mediator.UnitTests` |
| `SendManyAsync_NestedBatch_ShouldThrowInvalidOperationException` | `LiteBus.Mediator.UnitTests` |
| `TrySendManyAsync_WithThreeCommands_ShouldCommitDomainAndOutboxRowsTogether` | `LiteBus.Storage.IntegrationTests` |
| `TrySendManyAsync_WhenSecondCommandIsDenied_ShouldRollBackDomainAndOutboxRows` | `LiteBus.Storage.IntegrationTests` |
| `TrySendManyAsync_WithAnsweredCommand_ShouldCommitWithoutStopping` | `LiteBus.Storage.IntegrationTests` |
| `TrySendManyAsync_WhenBatchCommits_ShouldCompleteIdempotencyKeys` | `LiteBus.Storage.IntegrationTests` |
| `TrySendManyAsync_WhenBatchRollsBack_ShouldReleaseIdempotencyKeysAndRerunHandlers` | `LiteBus.Storage.IntegrationTests` |
| `TrySendManyAsync_WhenBatchCommits_ShouldWriteAuditRecordsWithBatchId` | `LiteBus.Storage.IntegrationTests` |
| `TrySendManyAsync_WhenBatchRollsBack_ShouldRewriteAuditRecordsForRollback` | `LiteBus.Storage.IntegrationTests` |
| `SendManyAsync_WhenHandlerStartsNestedBatch_ShouldThrowAndRollBack` | `LiteBus.Storage.IntegrationTests` |

### Untested

- Batch activity and histogram emission under the opt-in telemetry flags.
- Lock contention and throughput of large batches against a live database.

### Out-of-Scope

- Dependent command sequences that pass one command's result into the next.
- Event and query batching, and multi-message inbox commits.
- Distributed transaction protocols.

## References

- [Command module](../../concepts/commands.md)
- [In-process idempotency](../../concepts/idempotency.md)
- [Auditing](../../concepts/auditing.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Domain events and unit of work](../../reliable-messaging/domain-events-and-unit-of-work.md)
- [Recurring commands](recurring-commands.md)
