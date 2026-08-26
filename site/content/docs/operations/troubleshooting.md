# Troubleshooting

This page maps the exceptions and surprising behaviors LiteBus produces to their cause and fix. Each entry names the exception type, where it is thrown, why, and what to change. Use it as a lookup when a message fails to mediate or a registration throws at startup. The behaviors here are grounded in [The Handler Pipeline](../concepts/handler-pipeline.md).

## InvalidOperationException (Transactional Writer Requires Active Transaction)

Message contains `requires an active PostgreSQL transaction`.

**Cause:** `ITransactionalInbox` or `ITransactionalOutbox` was used with `EnableAmbientTransactionProvider()` (default `RequireActiveTransaction`), but no transaction was active in `IPostgreSqlTransactionProvider`.

**Fix:**

1. Open connection and `BeginTransactionAsync` at unit-of-work start; implement `TryGetCurrent` on your scoped provider.
2. Confirm the handler injects `ITransactionalOutbox`, not `IOutbox`.

See [Transactional messaging writes](../reliable-messaging/transactional-writes.md) and the troubleshooting section there.

## NoHandlerFoundException

Message: `No handler found for message type '<Type>'`.

Thrown by the mediator when it cannot resolve a descriptor for the message: the message type, or a handler for it, was never registered. For events it is thrown only when `EventMediationSettings.ThrowIfNoHandlerFound` is `true`; otherwise a published event with no handler returns silently.

Fix:

- Register the handler's assembly: `module.RegisterFromAssembly(typeof(SomeHandler).Assembly)`, or register the handler explicitly.
- Confirm the message implements the right marker (`ICommand`, `IQuery<T>`, `IEvent`) for the module you registered.
- In `WebApplicationFactory` tests, register LiteBus modules inside each factory's `ConfigureTestServices` so handler discovery uses that factory's own `IMessageRegistry`. See [Testing LiteBus](../testing/application-testing.md).

## MultipleHandlerFoundException

Message: `<Type> has <n> handlers registered.`

Thrown by the single-handler strategies for commands and queries when more than one main handler matches. Commands and queries must resolve to exactly one main handler. This is checked before any handler runs.

Fix:

- Remove the duplicate handler, or merge the two into one.
- Check you did not register the same assembly twice, or register a handler both explicitly and by assembly scan.
- If you intended fan-out to multiple handlers, the message should be an event, not a command or query. Events broadcast to all matching handlers. See [Event Module](../concepts/events.md).

## UnsupportedOpenGenericHandlerException

Message: `Open generic handler type '<Type>' declares <n> generic parameters. LiteBus supports open generic handlers with exactly one generic parameter.`

Thrown at registration when an open generic handler declares more than one type parameter, such as `MyHandler<TCommand, TResult>`. LiteBus closes open generic handlers only when they have exactly one type parameter.

Fix:

- Reduce the handler to a single type parameter, for example `ICommandPreHandler<T>`.
- If you need the result type, read it inside the handler rather than as a second type parameter, or use a concrete handler. See [Open Generic Handlers](../concepts/open-generic-handlers.md).

## A Gate Did Not Stop the Pipeline

Stopping the pipeline is a return value, not an exception. A pre-handler stops it only when it implements a gate contract such as `ICommandGate<TCommand>` or `IQueryGate<TQuery, TResult>` and returns a stopping directive from `DecideAsync`.

Common causes when a decision appears to be ignored:

- The handler implements the plain `ICommandPreHandler<TCommand>` contract, which cannot stop the pipeline by design.
- The handler was not registered. Every module builder gates assembly scanning on a handler-contract allowlist, so a handler implementing an unrecognized contract is skipped silently.
- The directive was constructed but not returned.

To skip the post-handlers after the work has already run, call `IExecutionContext.SuppressPostHandlers()` instead. That reports `MessageOutcome.Succeeded`, because the main handler ran.

## LiteBusConfigurationException When a Gate Stops a Result Message

Thrown when a gate stops a result-returning command or query without supplying a result. The caller is owed a value, so the directive must carry it.

Fix: implement the typed gate, `ICommandGate<TCommand, TCommandResult>` or `IQueryGate<TQuery, TQueryResult>`, and return `PipelineDirective<TResult>.ShortCircuit(result)`. The compiler then requires the result, and the exception message names the contract to use. See [The Handler Pipeline](../concepts/handler-pipeline.md).

Reference `LiteBus.Analyzers` to catch this at build time. `ICommand<TResult>` derives from `ICommand`, so the untyped contract compiles for a message that produces a result; `LB1019` reports the declaration and names the typed contract to use instead. See [Analyzers](../reference/analyzers.md).

## LiteBusMessageDeniedException Reached the Caller

A gate refused the message through `Deny(reason)` and supplied no result, so there was nothing to hand back. This is a decision rather than a fault: it does not reach error handlers, the mediation reports `MessageOutcome.Denied`, and an audit trail records a denial with the reason.

If the caller should receive a value instead of an exception, use the typed gate and `Deny(reason, result)` to hand back a refusal result.

## An Audit Record Is Missing for a Cancelled or Failed Mediation

The completion stage is not cancellable and its handlers receive `CancellationToken.None`, so cancellation alone does not drop a record. Check in this order:

- The message declares no audited position. An exempt or undeclared message produces no record.
- No `IAuditTrail` is registered. Run the `litebus.audit.trail` diagnostic probe, which reports unhealthy in that case.
- The trail threw while the mediation was already failing. That fault cannot replace the original exception, so it is attached to it: read `exception.Data[MediationExceptionData.SuppressedCompletionFaults]`, which holds an `IReadOnlyList<Exception>`.

## LB1004: Command with Result Scheduled to Inbox

Diagnostic: `LB1004` (analyzer error) or runtime failure when `IInbox.AcceptAsync` is invoked with a command that implements `ICommand<TResult>`. The inbox discards handler results when it replays a message later, so only result-less `ICommand` types can be stored.

Fix: store a result-less command, and query for the outcome separately when a caller needs it. Reference `LiteBus.Analyzers` so LB1004 catches invalid inbox writes at compile time. See [Inbox](../reliable-messaging/inbox.md) and [Analyzers](../reference/analyzers.md).

## Swallowed Exception: An Error-Handler That Does Not Rethrow

Behavior, not an exception. When a stage throws and at least one error-handler is registered, the error-handlers run and the exception is considered handled unless one rethrows. An error-handler that only logs silently swallows the failure, and a result-returning caller then receives whatever partial result existed, possibly `null`.

Fix: rethrow from the error-handler (`throw exception;`) unless you intend to swallow. See [The Handler Pipeline](../concepts/handler-pipeline.md#error-propagation).

## Event Handler Did Not Run

Behavior. A published event ran no handler. Causes:

- No handler is registered for the event type, and `ThrowIfNoHandlerFound` is `false`, so the publish returned silently. Set it to `true` in tests to make this fail loudly.
- A tag or predicate filtered the handler out. Check the tags passed to `PublishAsync` against the handler's `[HandlerTag]`. See [Handler Filtering](../concepts/handler-filtering.md).
- A pre-handler threw before the main handlers ran, so the broadcast stopped. Check error-handler behavior above.

## Handlers Ran in an Unexpected Order

Behavior. Within a stage, handlers run in ascending `[HandlerPriority]` (default `0`). Across the onion, global pre-handlers run before specific ones and specific post-handlers run before global ones. For events, priority groups and within-group execution follow the concurrency switches on `EventMediationSettings.Execution`; parallel execution makes order non-deterministic.

Fix: set explicit priorities, or switch event execution to `Sequential`. See [Handler Priority](../concepts/handler-priority.md).

## InvalidOperationException from Inbox or Outbox Hosting

Thrown when the generic host builds `InboxProcessorBackgroundService` or `OutboxProcessorBackgroundService` and DI cannot resolve required dependencies. Common causes:

- `EnableInboxProcessor()` or `EnableOutboxProcessor()` is set but no `IInboxDispatcher` or `IOutboxDispatcher` is registered (`UseInProcessDispatch` or a broker-specific `Use*Dispatch`).
- Storage is missing (`UsePostgreSqlStorage`, `UseInMemoryStorage`, etc. inside the module builder).
- The core module was not registered.
- A broker dispatch or ingress adapter is present without its matching root `Add*Transport(...)` registration.

Fix: register core module, storage, and dispatch before enabling processor background services. For PostgreSQL with `EnsureSchemaCreationOnStartup`, schema initialization runs before processor background services start. See [Hosted services](../architecture/hosted-services.md) and [Reliable Messaging](../reliable-messaging/README.md).

## PostgreSQL Integration Tests Reported as Skipped

Behavior. `LiteBus.Storage.PostgreSql.IntegrationTests` uses Testcontainers and needs Docker. Without Docker the tests are skipped with a message saying so.

Fix: start Docker Desktop or the Docker daemon, then rerun `dotnet test LiteBus.slnx`. To run unit tests only: `dotnet test LiteBus.slnx --filter "FullyQualifiedName!~PostgreSql"`.

## Next

Read [Handler Resolution Internals](../architecture/handler-resolution.md) to understand how the registry links handlers and why these exceptions fire when they do.
