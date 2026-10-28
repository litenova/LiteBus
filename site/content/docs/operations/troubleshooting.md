# Troubleshooting

LiteBus reports mediation and registration failures as exceptions, silent handlers, or failed registrations. This page maps each symptom to its cause and fix. Use it as a lookup when a message fails to mediate or a registration throws at startup.

> **This page is for** developers who see a LiteBus exception, a silent handler, or a failed registration and need the fix.
>
> **After reading it** you can match your symptom to its cause and apply the fix. Continue with [Handler Resolution Internals](../architecture/handler-resolution.md).

The behaviors here follow [The Handler Pipeline](../concepts/handler-pipeline.md).

## Durable Messaging

This section covers inbox, outbox, and transactional writer failures. For the state model behind these entries, see [Message States and Alerting](message-states.md).

### Transactional Writer Requires an Active Transaction

You see `InvalidOperationException` with text that contains `requires an active PostgreSQL transaction`.

Cause: you used `ITransactionalInbox` or `ITransactionalOutbox` with `EnableAmbientTransactionProvider`. That provider defaults to `RequireActiveTransaction`, but no transaction was active in `IPostgreSqlTransactionProvider`.

Fix:

1. Open the connection and call `BeginTransactionAsync` at unit-of-work start. Implement `TryGetCurrent` on your scoped provider.
2. Confirm the handler injects `ITransactionalOutbox`, not `IOutbox`.

See [Transactional Messaging Writes](../reliable-messaging/transactional-writes.md).

### Inbox Message Dead-Lettered Without Retrying

A message reaches the dead letter on its first attempt with no retries.

Cause: a refusal and a missing handler produce the same outcome on every attempt, so both processors retire such a message on the first attempt. `MediationExceptionFilters.IsRetryableDispatchException` inspects the inner exception chain, so a refusal or missing handler wrapped in a dispatcher exception is still retired on the first attempt.

Fix: read the dead-letter error text. `LiteBusMessageDeniedException` and `LiteBusMessageInvalidException` are decisions about the message itself. `NoHandlerFoundException` means nothing is registered to handle it. Fix the registration or the message instead of raising the retry count.

### Command with Result Scheduled to Inbox

When `IInbox.AcceptAsync` receives a command that implements `ICommand<TResult>`, you see analyzer error `LB1004` or a runtime failure.

Cause: the inbox discards handler results when it replays a message later, so only result-less `ICommand` types can be stored.

Fix: store a result-less command. When a caller needs the outcome, query for it separately. Reference `LiteBus.Analyzers` so `LB1004` catches invalid inbox writes at compile time. See [Inbox](../reliable-messaging/inbox.md).

### Processor Service Failed to Build

When the generic host builds `InboxProcessorBackgroundService` or `OutboxProcessorBackgroundService`, you see `InvalidOperationException`.

Cause: dependency injection cannot resolve a required dependency. Common cases are an enabled processor without a dispatcher (`UseInProcessDispatch` or a broker `Use*Dispatch`), missing storage (`UsePostgreSqlStorage` or `UseInMemoryStorage` inside the module builder), a missing core module, or a broker dispatch or ingress adapter without its matching root `Add*Transport` registration.

Fix: register the core module, storage, and dispatch before you enable processor background services. For PostgreSQL with `EnsureSchemaCreationOnStartup`, schema initialization runs before processor background services start. See [Hosted Services](../architecture/hosted-services.md) and [Reliable Messaging](../reliable-messaging/README.md).

### PostgreSQL Tests Reported as Skipped

`LiteBus.Storage.IntegrationTests` are skipped with a message about Docker.

Cause: those tests use Testcontainers and need Docker, so without Docker they skip.

Fix: start Docker Desktop or the Docker daemon, then rerun `dotnet test LiteBus.slnx`. To run unit tests only, run `dotnet test LiteBus.slnx --filter "FullyQualifiedName!~PostgreSql"`.

```powershell
dotnet test LiteBus.slnx --filter "FullyQualifiedName!~PostgreSql"
```

## Mediation Pipeline

This section covers command, query, and event mediation failures. For the stage model behind these entries, see [The Handler Pipeline](../concepts/handler-pipeline.md).

### No Handler Found for Message Type

You see `NoHandlerFoundException` with `No handler found for message type '<Type>'`.

Cause: the mediator cannot resolve a descriptor for the message because the message type or a handler for it was never registered. For an immediate publish this throws only when `EventMediationSettings.ThrowIfNoHandlerFound` is true. Otherwise a published event with no handler returns silently. Outbox in-process dispatch is the exception. It requires a handler by default because a durable dispatch has no caller to observe the silence. Set `UseInProcessDispatch(dispatch => dispatch.RequireHandler = false)` only when publishing to zero handlers is a state your application allows.

Fix:

1. Register the handler assembly with `module.RegisterFromAssembly(typeof(SomeHandler).Assembly)`, or register the handler explicitly.
2. Confirm the message implements the right marker (`ICommand`, `IQuery<T>`, `IEvent`) for the module you registered.
3. In `WebApplicationFactory` tests, register LiteBus modules inside each factory `ConfigureTestServices` so discovery uses that factory `IMessageRegistry`. See [Testing LiteBus](../testing/application-testing.md).

### More than One Handler for a Command or Query

You see `MultipleHandlerFoundException` with `<Type> has <n> handlers registered.`

Cause: the single-handler strategies for commands and queries found more than one main handler. Commands and queries must resolve to exactly one main handler. The mediator checks this before any handler runs.

Fix:

1. Remove the duplicate handler, or merge the two into one.
2. Check that you did not register the same assembly twice, or register a handler both explicitly and by assembly scan.
3. If you intended fan-out to multiple handlers, use an event instead of a command or query. Events broadcast to all matching handlers. See [Event Module](../concepts/events.md).

### Open Generic Handler with Unsupported Arity

You see `UnsupportedOpenGenericHandlerException` naming your open generic handler type and its generic parameter count.

Cause: the handler declares a type-parameter shape the registry cannot close at registration. One parameter always binds to the message type. Two parameters bind only when the handler implements a contract that takes both in order, such as `ICommandPostHandler<TCommand, TCommandResult>`, where the second binds to the result type the message declares. A second parameter of your own invention, such as `TContext` or `TStore`, has nothing to bind to.

Fix:

1. To reach the typed result, implement the two-parameter contract, for example `ICommandPostHandler<TCommand, TCommandResult>` with `where TCommand : ICommand<TCommandResult>`.
2. For an auxiliary parameter, take the dependency through the constructor instead, or use a concrete handler. See [Open Generic Handlers](../concepts/open-generic-handlers.md).

### Decision Did Not Stop the Pipeline

Your guard, validator, or shortcut ran but the pipeline continued.

Cause: stopping the pipeline is a return value, not an exception. A pre-stage handler stops it only when it implements a guard contract such as `ICommandGuard<TCommand>` and returns `Verdict.Deny` from `DecideAsync`, a validator contract such as `ICommandValidator<TCommand>` and returns `Validity.Invalid` from `ValidateAsync`, or a shortcut contract such as `IQueryShortcut<TQuery, TResult>` and returns an answer from `TryAnswerAsync`. When the pipeline continues anyway, one of these is usually the cause:

- The handler implements plain `ICommandPreHandler<TCommand>`, which cannot stop the pipeline by design.
- The handler was never registered, since assembly scanning skips contracts outside the module allowlist.
- The handler constructs a decision without returning it.
- An earlier stage won, since guards run before validators, validators before shortcuts, and shortcuts before pre-handlers.

Fix: return the decision value from the handler. To skip post-handlers after the work already ran, call `IExecutionContext.SuppressPostHandlers()` instead, which reports `MediationOutcome.Succeeded`.

### Untyped Shortcut on a Result Message

You see `PipelineContractException` for an untyped shortcut on a command or query that produces a result.

Cause: the untyped shortcut contract cannot carry a value, so it can never answer a result message. Registration reports it when the shortcut names the message directly. Dispatch reports it when the shortcut was registered against a base type or interface, since a shortcut for `ICommand` is valid for every result-less command beneath it and nothing before dispatch knows which message it will answer.

Fix: implement the typed shortcut, `ICommandShortcut<TCommand, TCommandResult>` or `IQueryShortcut<TQuery, TQueryResult>`. Return `Shortcut<TResult>.Answer(result)`. A shortcut registered against a base type must return `Shortcut.None` for messages beneath it that produce a result. Guards and validators need no change because a denial never owes the caller a result. Reference `LiteBus.Analyzers` so `LB1019` catches the declaration at build time. See [Analyzers](../reference/analyzers.md).

### Guard or Validator Exception Reached the Caller

You see `LiteBusMessageDeniedException` or `LiteBusMessageInvalidException` at the caller.

Cause: a guard refused the message or a validator reported it malformed. No refusal mapper covers it. Both are decisions rather than faults. Neither reaches error handlers. The mediation reports `MediationOutcome.Denied` or `Invalid`. `LiteBusMessageInvalidException.Failures` carries every failure the validator stage collected. `LiteBusMessageDeniedException.DeniedBy` names the guard. `LiteBusMessageInvalidException.RejectedBy` names the validator. Each message includes that handler name.

Fix: register an `IMessageRefusalMapper<TMessage, TMessageResult>` when the caller should receive a value instead of an exception. One registration against `ICommand` or `IQuery` covers the whole axis. A mapper against a concrete message type overrides it. A message with no result, and any event, has nothing a mapper could return, so a refusal there always raises. When the caller should branch instead, call `TrySendAsync` or `TryQueryAsync`. Map the returned `MediationResult` to a 403 or 400 without catching anything. A genuine fault still throws.

### Finding the Guard That Denied a Message

You need to know which guard refused a message.

Cause: several guards cover the message and the reason alone does not identify one.

Fix: read `LiteBusMessageDeniedException.DeniedBy` for the concrete guard type, or read the exception message which names that guard. For a validation failure, read `LiteBusMessageInvalidException.RejectedBy` the same way. The decision counter also tags the handler in `litebus.decided_by`.

### More than One Refusal Mapper

You see `PipelineContractException` naming more than one refusal mapper.

Cause: two mappers that produce the same result type are registered at the same level of specificity, so the winner would depend on assembly scanning order.

Fix: remove one mapper, or register the one that should win against the concrete message type, which takes precedence over a mapper registered for a base type.

### Audit Record Missing for a Cancelled or Failed Mediation

No audit record exists for a mediation you expected to be audited.

Cause: check these in order. First, the message may declare no audited position, so an exempt or undeclared message produces no record. Second, no `IAuditTrail` may be registered. Run the `litebus.audit.trail` diagnostic probe, which reports unhealthy in that case. Third, the trail may have thrown while the mediation was already failing, so its fault is attached to the original exception in `exception.Data[MediationExceptionData.SuppressedCompletionFaults]` as an `IReadOnlyList<Exception>`.

Fix: declare the audited position, register a trail, or read the suppressed faults collection. The completion stage is not cancellable. Its handlers receive `CancellationToken.None`, so cancellation alone does not drop a record.

### Error Handler Swallowed the Exception

A stage threw but the caller received a partial result or null with no error.

Cause: when a stage throws and at least one error handler is registered, the error handlers run. The exception counts as handled unless one rethrows. An error handler that only logs silently swallows the failure.

Fix: rethrow from the error handler with `throw;` unless you intend to swallow. Rethrowing with `throw exception;` resets the stack trace. See [The Handler Pipeline](../concepts/handler-pipeline.md).

### Event Handler Did Not Run

A published event ran no handler and returned normally.

Cause: one of three things happened. No handler is registered for the event type and `ThrowIfNoHandlerFound` is false, so the publish returned silently. A tag or predicate filtered the handler out, so check the tags passed to `PublishAsync` against the handler `[HandlerTag]`. Or a pre-handler threw before the main handlers ran.

Fix:

1. Set `ThrowIfNoHandlerFound` to true in tests to make the first case fail loudly.
2. Check tags in [Handler Filtering](../concepts/handler-filtering.md).
3. Check error-handler behavior in the swallowed-exception entry above.

### Handlers Ran in an Unexpected Order

Handlers ran but not in the order you expected.

Cause: within a stage, handlers run in ascending `[HandlerPriority]` with registration order breaking ties. Across the pipeline, global pre-handlers run before specific ones. Specific post-handlers run before global ones. The completion stage merges global and specific sets. It orders by priority alone. For events, priority groups and within-group execution follow `EventMediationSettings.Execution`. Parallel execution makes order non-deterministic.

Fix: set explicit priorities, or switch event execution to `Sequential`. See [Handler Priority](../concepts/handler-priority.md).

## Next

- [Handler Resolution Internals](../architecture/handler-resolution.md)
- [The Handler Pipeline](../concepts/handler-pipeline.md)
- [Hosted Services](../architecture/hosted-services.md)
