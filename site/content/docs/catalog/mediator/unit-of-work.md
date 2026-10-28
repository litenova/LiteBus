# Unit of Work

- **ID**: `mediator.unit-of-work`
- **Name**: Unit of work
- **Maturity**: GA
- **Summary**: Commits the work one mediation staged from a completion handler at `HandlerPriorities.UnitOfWork`, after every successful command or query.
- **Guide**: [Domain events and unit of work](../../reliable-messaging/domain-events-and-unit-of-work.md)

## What It Does

The application implements `IUnitOfWork`, whose single `CommitAsync` method commits the work the handler staged, and registers it with `UseUnitOfWork<TUnitOfWork>` on the command or query module builder. LiteBus resolves it from the mediation scope and calls it once per mediation that ended with `MediationOutcome.Succeeded`, from a completion handler at `HandlerPriorities.UnitOfWork`. That position runs after every LiteBus completion handler, so a record the audit writer staged at `HandlerPriorities.Observability` is part of the same commit.

The commit does not belong in a post-handler. A post-handler never runs when the main handler throws, so a commit placed there cannot gate on a failure, and everything LiteBus writes afterwards lands outside the transaction. The completion stage runs on every path, so the shipped handler gates on the outcome instead. A denial, a validation failure, a shortcut answer, a fault, and a cancellation all end without committing. A shortcut answer does not commit even though it reports success, because the main handler never ran and staged nothing.

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

| API | Role |
| --- | --- |
| `IUnitOfWork.CommitAsync(CancellationToken)` | Commits the work the mediation staged. The token is `CancellationToken.None` because the completion stage is not cancellable |
| `CommandModuleBuilder.UseUnitOfWork<TUnitOfWork>()` | Registers the commit for the command axis |
| `QueryModuleBuilder.UseUnitOfWork<TUnitOfWork>()` | Registers the commit for the query axis |
| `HandlerPriorities.UnitOfWork` | The commit position, above `ReservedCeiling` and after the audit writer |
| `MartenUnitOfWork` | The shipped commit over the scoped Marten session, from `LiteBus.Commands.Storage.Marten` |

## Packages

- `LiteBus.Messaging.Abstractions` (`IUnitOfWork`)
- `LiteBus.Commands` (command commit handler)
- `LiteBus.Queries` (query commit handler)
- `LiteBus.Commands.Storage.Marten` (`MartenUnitOfWork`)

## Requires

- `mediator.commands` or `mediator.queries`
- `mediator.handler-pipeline`
- `mediator.handler-priority`

## Invariants

- `CommitAsync` runs once per mediation that ended `Succeeded`, and never for a denial, a validation failure, a shortcut answer, a fault, or a cancellation.
- LiteBus never opens or owns the transaction. The application opens it, stages work into it, and this commit closes it, while releasing uncommitted work stays the implementation's own concern.
- Calling `UseUnitOfWork` twice on one builder throws `LiteBusConfigurationException`. Registering it on commands and queries with different types is allowed, with each axis committing its own, but one type is preferred so the shared `IUnitOfWork` service resolves unambiguously.
- The implementation registers as scoped, as itself plus a first-wins `IUnitOfWork` mapping.
- A commit that throws fails the mediation with that exception, so a concurrency conflict reaches the caller instead of silently losing the write.

## Non-Goals

- Transaction demarcation: LiteBus opens nothing and rolls back nothing.
- Cross-mediation or cross-process transaction coordination.
- Automatic retry of a failed commit.

## Observability

No unit-of-work-specific meter or activity source. A throwing commit surfaces as the mediation failure, so it is visible in mediation telemetry and in the audit record for the mediation.

Operational alternatives:
- Log commit duration in the `IUnitOfWork` implementation.
- Assert the commit position with `IMessageReader.Explain` in composition tests.

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

### Untested

- Commit ordering against a staged outbox drain and the audit writer in one mediation.
- A commit that opens the transaction lazily on first staged write.

### Out-of-Scope

- ORM session lifetime management outside the mediation scope.
- Distributed transaction protocols.

## References

- [Domain events and unit of work](../../reliable-messaging/domain-events-and-unit-of-work.md)
- [Auditing](../../concepts/auditing.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
