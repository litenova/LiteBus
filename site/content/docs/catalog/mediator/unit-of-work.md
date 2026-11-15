# Unit of Work

- **ID**: `mediator.unit-of-work`
- **Name**: Unit of work
- **Maturity**: GA
- **Summary**: Commits the work one mediation staged from a completion handler at `HandlerPriorities.UnitOfWork`, after every successful command or query, with an optional boundary hook that opens and rolls back the transaction, and one commit for a command batch.
- **Guide**: [Domain events and unit of work](../../reliable-messaging/domain-events-and-unit-of-work.md)

## What It Does

The application implements `IUnitOfWork`, whose single `CommitAsync` method commits the work the handler staged, and registers it with `UseUnitOfWork<TUnitOfWork>` on the command or query module builder. LiteBus resolves it from the mediation scope and calls it once per mediation that ended with `MediationOutcome.Succeeded`, from a completion handler at `HandlerPriorities.UnitOfWork`. That position runs after every LiteBus completion handler, so a record the audit writer staged at `HandlerPriorities.Observability` is part of the same commit.

The commit does not belong in a post-handler. A post-handler never runs when the main handler throws, so a commit placed there cannot gate on a failure, and everything LiteBus writes afterwards lands outside the transaction. The completion stage runs on every path, so the shipped handler gates on the outcome instead. A denial, a validation failure, a shortcut answer, a fault, and a cancellation all end without committing. A shortcut answer does not commit even though it reports success, because the main handler never ran and staged nothing.

A unit of work that needs an explicit transaction implements `IUnitOfWorkBoundary` beside `IUnitOfWork`. LiteBus calls `BeginAsync` once per mediation, after guards, validators and shortcuts have let the message through and before the first pre-handler runs, so a guard denial, a validation failure, or a shortcut answer opens nothing. On `MediationOutcome.Succeeded`, `CommitAsync` runs as above. On any other outcome after `BeginAsync` ran (a fault, a cancellation, or a refusal raised from a pre-handler or handler), the unit-of-work completion handler calls `RollbackAsync` with `CancellationToken.None`. A `RollbackAsync` that throws is recorded under `MediationExceptionData.SuppressedCompletionFaults` and never replaces the original failure. The call runs from a pre-handler that `UseUnitOfWork` registers only when the unit of work implements the interface, at the lowest priority so no application pre-handler can write first, and it shows in `IMessageReader.Explain` output. A unit of work that does not implement the interface behaves as before: LiteBus commits on success and releases nothing itself. LiteBus still references no ORM or driver; the application opens and rolls back the transaction inside `BeginAsync` and `RollbackAsync`.

Inside a command batch (`ICommandBatchMediator.SendManyAsync`), the per-command unit-of-work completion handler neither commits nor rolls back. The batch calls `BeginAsync` once before its first command when the unit of work implements `IUnitOfWorkBoundary`, runs every command in one dispatch scope that shares the unit of work, the transactional outbox and the domain event collector, then calls `CommitAsync` once after all commands passed. A command passes on `Succeeded` or `Answered`, so a shortcut answer such as an idempotent repeat does not stop the batch. The first command that is `Denied`, `Invalid`, faulted or cancelled stops the batch and no later command runs. When the batch stops, or `CommitAsync` throws, the batch calls `RollbackAsync` when the hook is implemented and reports `RolledBack`. When no unit of work is registered, the batch still runs in one scope and stops on the first failure, but atomicity then depends entirely on the application. See [Command batches](command-batches.md).

When a unit of work is registered, idempotency keys settle around the commit rather than always before it. A store without the `ISeparatelyCommittedIdempotencyStore` marker settles at `HandlerPriorities.Persistence`, before the commit, so its settle stages into the same transaction that applies the change. A store with the marker commits its own writes on its own connection, so the idempotency completion handler records the pending settlement instead of settling, and the unit-of-work handler calls `CompleteAsync` after `CommitAsync` returns; when `CommitAsync` throws, it calls `ReleaseAsync` and rethrows the commit exception unchanged, so the retry runs the handler again instead of being answered as a repeat. A marked store with no registered unit of work settles at `Persistence` as before, and a mediation that did not succeed releases a marked store's claim immediately. A post-commit `CompleteAsync` that throws is retried up to three times with a fixed backoff of about 50, 100 and 200 milliseconds, then logged as a warning naming the key without failing the mediation, because the data is already committed. The key stays claimed until the store's claim timeout, after which a repeat runs the handler again. Between the commit and the settlement a crash leaves committed data whose key is still only claimed. Both shipped stores implement the marker. Inside a command batch no command settles per command: `CompleteAsync` for every pending claim runs before the batch `CommitAsync` for an unmarked store and after it for a marked store, and `ReleaseAsync` runs for every pending claim on rollback. See [Command idempotency](command-idempotency.md).

After `CommitAsync` returns and the deferred idempotency settlement is complete, every `IUnitOfWorkCommitListener` resolved from the mediation scope is notified in registration order through `OnCommittedAsync`. A command batch notifies them once, after its single commit, and never per command. A listener runs after the data is committed, so a listener that throws is logged and does not fail the mediation. Immediate outbox dispatch is one listener, registered scoped by `EnableImmediateDispatch`.

## Public Surface

```csharp
public sealed class OrderUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _dbContext;

    public OrderUnitOfWork(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}

registry.AddCommands(commands => commands
    .RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly)
    .UseUnitOfWork<OrderUnitOfWork>());
```

A unit of work over an explicit transaction adds the boundary hook:

```csharp
public sealed class OrderUnitOfWork : IUnitOfWork, IUnitOfWorkBoundary
{
    private readonly AppDbContext _dbContext;
    private IDbContextTransaction? _transaction;

    public OrderUnitOfWork(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        _transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        return _transaction is null
            ? Task.CompletedTask
            : _transaction.RollbackAsync(cancellationToken);
    }
}
```

| API | Role |
| --- | --- |
| `IUnitOfWork.CommitAsync(CancellationToken)` | Commits the work the mediation staged. The token is `CancellationToken.None` because the completion stage is not cancellable |
| `IUnitOfWorkBoundary.BeginAsync(CancellationToken)` | Opens the transaction before the first pre-handler, after guards, validators and shortcuts passed |
| `IUnitOfWorkBoundary.RollbackAsync(CancellationToken)` | Discards the staged work after a mediation that began and did not succeed. The token is `CancellationToken.None` |
| `IUnitOfWorkCommitListener.OnCommittedAsync(CancellationToken)` | Runs after the commit returns, in registration order; a batch notifies once after its single commit |
| `ISeparatelyCommittedIdempotencyStore` | Marks a store that commits its own writes, so its keys settle after `CommitAsync` instead of at `Persistence` |
| `CommandModuleBuilder.UseUnitOfWork<TUnitOfWork>()` | Registers the commit for the command axis, and the begin pre-handler when the type implements `IUnitOfWorkBoundary` |
| `QueryModuleBuilder.UseUnitOfWork<TUnitOfWork>()` | Registers the commit for the query axis, and the begin pre-handler when the type implements `IUnitOfWorkBoundary` |
| `HandlerPriorities.UnitOfWork` | The commit position, above `ReservedCeiling` and after the audit writer |
| `ICommandBatchMediator.SendManyAsync` / `TrySendManyAsync` | Runs several commands in one scope with one commit; the per-command handler commits nothing inside a batch |
| `MartenUnitOfWork` | The shipped commit over the scoped Marten session, from `LiteBus.Commands.Storage.Marten` |

## Packages

- `LiteBus.Messaging.Abstractions` (`IUnitOfWork`, `IUnitOfWorkBoundary`, `IUnitOfWorkCommitListener`, `ISeparatelyCommittedIdempotencyStore`)
- `LiteBus.Commands.Abstractions` (`ICommandBatchMediator`)
- `LiteBus.Commands` (command commit handler, `CommandBatchMediator`)
- `LiteBus.Queries` (query commit handler)
- `LiteBus.Commands.Storage.Marten` (`MartenUnitOfWork`)

## Requires

- `mediator.commands` or `mediator.queries`
- `mediator.handler-pipeline`
- `mediator.handler-priority`
- `mediator.command-batches` (one commit for the whole batch)
- `mediator.command-idempotency` (settlement around the commit)

## Invariants

- `CommitAsync` runs once per mediation that ended `Succeeded`, and never for a denial, a validation failure, a shortcut answer, a fault, or a cancellation.
- LiteBus never opens or owns the transaction itself. The application opens it in `BeginAsync`, stages work into it, commits it in `CommitAsync`, and discards it in `RollbackAsync`; LiteBus only marks the points where each runs.
- `BeginAsync` runs once per mediation that implements the hook, before the first pre-handler. A guard denial, a validation failure, or a shortcut answer opens nothing, and a unit of work without the interface sees no begin or rollback call.
- `RollbackAsync` runs with `CancellationToken.None` on any outcome other than `Succeeded` after `BeginAsync` ran. A rollback that throws is recorded under `MediationExceptionData.SuppressedCompletionFaults` and never replaces the original failure.
- Inside a command batch the per-command handler neither commits nor rolls back. The batch calls `BeginAsync` once before its first command, `CommitAsync` once after all commands passed, and `RollbackAsync` when the batch stops or the commit throws.
- A store without the marker settles at `HandlerPriorities.Persistence`, before the commit. A store with the marker and a registered unit of work settles after `CommitAsync` returns, and a failed commit releases its claim and rethrows the commit exception unchanged.
- A post-commit `CompleteAsync` that throws is retried up to three times with a fixed backoff of about 50, 100 and 200 milliseconds, then logged as a warning naming the key without failing the mediation. The key stays claimed until the store's claim timeout.
- Commit listeners run in registration order after the commit and the deferred settlement, once per batch. A listener that throws is logged and does not fail the mediation.
- Calling `UseUnitOfWork` twice on one builder throws `LiteBusConfigurationException`. Registering it on commands and queries with different types is allowed, with each axis committing its own, but one type is preferred so the shared `IUnitOfWork` service resolves unambiguously.
- The implementation registers as scoped, as itself plus a first-wins `IUnitOfWork` mapping.
- A commit that throws fails the mediation with that exception, so a concurrency conflict reaches the caller instead of silently losing the write.

## Non-Goals

- Owning the transaction object: LiteBus marks the begin, commit and rollback points through `IUnitOfWork` and `IUnitOfWorkBoundary`, but the application opens, commits and rolls back the transaction, and LiteBus references no ORM or driver.
- Cross-process or cross-batch transaction coordination. A command batch commits once for its own commands in one process; nothing coordinates two batches or two hosts.
- Automatic retry of a failed commit.

## Observability

No unit-of-work-specific meter or activity source. A throwing commit surfaces as the mediation failure, so it is visible in mediation telemetry and in the audit record for the mediation. A throwing rollback is recorded under `MediationExceptionData.SuppressedCompletionFaults` without replacing the original failure. A post-commit settlement that fails after its retries, and a commit listener that throws, each log a warning naming the key or the listener while the mediation still reports success.

Operational alternatives:
- Log commit duration in the `IUnitOfWork` implementation.
- Assert the commit position with `IMessageReader.Explain` in composition tests.
- Read the batch duration and size instruments on the command-batches page for batch commit behavior.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Commit_runs_once_after_a_successful_command` | `LiteBus.Mediator.UnitTests` |
| `Commit_does_not_run_after_a_guard_denial` | `LiteBus.Mediator.UnitTests` |
| `Commit_does_not_run_after_a_validation_failure` | `LiteBus.Mediator.UnitTests` |
| `Commit_does_not_run_after_a_handler_exception` | `LiteBus.Mediator.UnitTests` |
| `Commit_does_not_run_when_a_shortcut_answers` | `LiteBus.Mediator.UnitTests` |
| `Commit_runs_between_lower_and_higher_priority_completion_handlers` | `LiteBus.Mediator.UnitTests` |
| `A_throwing_commit_fails_SendAsync_with_that_exception` | `LiteBus.Mediator.UnitTests` |
| `Calling_UseUnitOfWork_twice_on_the_command_builder_throws` | `LiteBus.Mediator.UnitTests` |
| `Registering_different_unit_of_work_types_on_commands_and_queries_is_allowed` | `LiteBus.Mediator.UnitTests` |
| `Commit_runs_once_after_a_successful_query` | `LiteBus.Mediator.UnitTests` |
| `Commit_does_not_run_after_a_query_guard_denial` | `LiteBus.Mediator.UnitTests` |
| `Commit_does_not_run_after_a_query_validation_failure` | `LiteBus.Mediator.UnitTests` |
| `Commit_does_not_run_after_a_query_handler_exception` | `LiteBus.Mediator.UnitTests` |
| `Commit_does_not_run_when_a_query_shortcut_answers` | `LiteBus.Mediator.UnitTests` |
| `Commit_runs_between_lower_and_higher_priority_query_completion_handlers` | `LiteBus.Mediator.UnitTests` |
| `A_throwing_commit_fails_QueryAsync_with_that_exception` | `LiteBus.Mediator.UnitTests` |
| `Calling_UseUnitOfWork_twice_on_the_query_builder_throws` | `LiteBus.Mediator.UnitTests` |
| `EfCoreUnitOfWorkBoundaryTests.TrySendAsync_ShouldCommitHandlerSavedRows_OnSuccess` | `LiteBus.Storage.IntegrationTests` |
| `EfCoreUnitOfWorkBoundaryTests.TrySendAsync_ShouldLeaveNoRows_WhenTheHandlerThrowsAfterSaving` | `LiteBus.Storage.IntegrationTests` |
| `EfCoreUnitOfWorkBoundaryTests.TrySendAsync_ShouldOpenNoTransaction_WhenAGuardDenies` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlIdempotencySettlementOrderTests.SendAsync_ShouldCommitTheOrderAndCompleteTheKeyAfterTheCommit` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlIdempotencySettlementOrderTests.SendAsync_WhenTheCommitFails_ShouldReleaseTheKeyAndThrowTheCommitException` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlIdempotencySettlementOrderTests.Retry_AfterAFailedCommit_ShouldRunTheHandlerAgainInsteadOfAnswering` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlIdempotencySettlementOrderTests.SendAsync_WithAnUnmarkedStore_WhenTheCommitFails_ShouldKeepTheKeyCompleted` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlIdempotencySettlementOrderTests.SendAsync_WhenPostCommitSettlementFails_ShouldStillSucceedWithCommittedData` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlIdempotencySettlementOrderTests.SendAsync_WithoutAUnitOfWork_ShouldCompleteTheKey` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlIdempotencySettlementOrderTests.SendAsync_WhenTheHandlerFails_ShouldReleaseTheKeyImmediately` | `LiteBus.Storage.IntegrationTests` |
| `MartenIdempotencySettlementOrderTests.SendAsync_ShouldCommitTheOrderAndCompleteTheKeyAfterTheCommit` | `LiteBus.Storage.IntegrationTests` |
| `MartenIdempotencySettlementOrderTests.SendAsync_WhenTheCommitFails_ShouldReleaseTheKeyAndThrowTheCommitException` | `LiteBus.Storage.IntegrationTests` |
| `MartenIdempotencySettlementOrderTests.Retry_AfterAFailedCommit_ShouldRunTheHandlerAgainInsteadOfAnswering` | `LiteBus.Storage.IntegrationTests` |
| `MartenIdempotencySettlementOrderTests.SendAsync_WithAnUnmarkedStore_WhenTheCommitFails_ShouldKeepTheKeyCompleted` | `LiteBus.Storage.IntegrationTests` |
| `MartenIdempotencySettlementOrderTests.SendAsync_WhenPostCommitSettlementFails_ShouldStillSucceedWithCommittedData` | `LiteBus.Storage.IntegrationTests` |
| `MartenIdempotencySettlementOrderTests.SendAsync_WithoutAUnitOfWork_ShouldCompleteTheKey` | `LiteBus.Storage.IntegrationTests` |
| `MartenIdempotencySettlementOrderTests.SendAsync_WhenTheHandlerFails_ShouldReleaseTheKeyImmediately` | `LiteBus.Storage.IntegrationTests` |
| `EfCoreCommandBatchTests.TrySendManyAsync_WithThreeCommands_ShouldCommitDomainAndOutboxRowsTogether` | `LiteBus.Storage.IntegrationTests` |
| `EfCoreCommandBatchTests.TrySendManyAsync_WhenSecondCommandIsDenied_ShouldRollBackDomainAndOutboxRows` | `LiteBus.Storage.IntegrationTests` |
| `EfCoreCommandBatchTests.TrySendManyAsync_WithAnsweredCommand_ShouldCommitWithoutStopping` | `LiteBus.Storage.IntegrationTests` |

### Untested

- Post-commit idempotency settlement retries against a real clock; the tests drive time with a `TimeProvider`.
- Ordering of several commit listeners when more than one is registered.
- A commit that opens the transaction lazily on first staged write.

### Out-of-Scope

- ORM session lifetime management outside the mediation scope.
- Distributed transaction protocols.

## References

- [Domain events and unit of work](../../reliable-messaging/domain-events-and-unit-of-work.md)
- [Command batches](command-batches.md)
- [Command idempotency](command-idempotency.md)
- [Auditing](../../concepts/auditing.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
