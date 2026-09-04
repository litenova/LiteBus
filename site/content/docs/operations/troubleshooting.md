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

Message: `Open generic handler type '<Type>' declares <n> generic parameters. LiteBus closes one, binding the message type, or two, binding the message type and the result type the message declares. Any other arity has nothing to bind to.`

Thrown at registration when an open generic handler declares a type-parameter shape the registry cannot close. One parameter is always fine. Two are fine when the handler implements a contract taking both of them in order, such as `ICommandPostHandler<TCommand, TCommandResult>`, because then the second binds to the result type the message declares. Two where the second is the handler's own invention, a `TContext` or a `TStore`, has nothing to bind to, because the registry closes by position.

Fix:

- To reach the typed result, implement the two-parameter contract: `ICommandPostHandler<TCommand, TCommandResult>` with `where TCommand : ICommand<TCommandResult>`.
- For an auxiliary parameter, take the dependency through the constructor instead, or use a concrete handler. See [Open Generic Handlers](../concepts/open-generic-handlers.md#reaching-the-typed-result).

## A Decision Did Not Stop the Pipeline

Stopping the pipeline is a return value, not an exception. A pre-stage handler stops it only when it implements a guard contract such as `ICommandGuard<TCommand>` and returns `Verdict.Deny` from `DecideAsync`, a validator contract such as `ICommandValidator<TCommand>` and returns `Validity.Invalid` from `ValidateAsync`, or a shortcut contract such as `IQueryShortcut<TQuery, TResult>` and returns an answer from `TryAnswerAsync`.

Common causes when a decision appears to be ignored:

- The handler implements the plain `ICommandPreHandler<TCommand>` contract, which cannot stop the pipeline by design.
- The handler was not registered. Every module builder gates assembly scanning on a handler-contract allowlist, so a handler implementing an unrecognized contract is skipped silently.
- The decision was constructed but not returned. `Verdict.Deny(...)` and `Shortcut.Answer(...)` have no effect until they are the return value.
- An earlier stage stopped first. Guards run before validators, validators before shortcuts, and shortcuts before pre-handlers, whatever priority each carries.

To skip the post-handlers after the work has already run, call `IExecutionContext.SuppressPostHandlers()` instead. That reports `MediationOutcome.Succeeded`, because the main handler ran.

## PipelineContractException for an Untyped Shortcut on a Result Message

The untyped shortcut contract cannot carry a value, so it cannot answer a command or query that produces one. This is reported at whichever of two points can prove it.

**At registration**, when the shortcut names the message directly. The message reads `Shortcut 'X' implements the untyped shortcut contract for 'Y', which produces 'Z'`. Registration linked the shortcut to a message whose main handler produces a result, which is enough to prove the shortcut can never answer it.

**At dispatch**, when the shortcut was registered against a base type or interface. A shortcut for `ICommand` is correct for every result-less command beneath it, so nothing before dispatch knows which message it will answer. The message names the shortcut that answered, so several globally registered shortcuts can still be told apart.

Fix, in both cases: implement the typed shortcut, `ICommandShortcut<TCommand, TCommandResult>` or `IQueryShortcut<TQuery, TQueryResult>`, and return `Shortcut<TResult>.Answer(result)`. The compiler then requires the result. A shortcut registered against a base type must return `Shortcut.None` for the messages beneath it that produce a result.

A guard or a validator needs no such change, because a denial never owes the caller a result. See [The Handler Pipeline](../concepts/handler-pipeline.md) and [Mediation Layer Design Rules](../architecture/mediation-design.md).

Reference `LiteBus.Analyzers` to catch this at build time. `ICommand<TResult>` derives from `ICommand`, so the untyped contract compiles for a message that produces a result; `LB1019` reports the declaration and names the typed contract to use instead. See [Analyzers](../reference/analyzers.md).

## LiteBusMessageDeniedException or LiteBusMessageInvalidException Reached the Caller

A guard refused the message, or a validator reported it malformed, and no refusal mapper covers it, so there was nothing to hand back. Both are decisions rather than faults: neither reaches error handlers, the mediation reports `MediationOutcome.Denied` or `Invalid`, and an audit trail records it accordingly. `LiteBusMessageInvalidException.Failures` carries every failure the validator stage collected.

If the caller should receive a value instead of an exception, register an `IMessageRefusalMapper<TMessage, TMessageResult>`. One registration against `ICommand` or `IQuery` covers the whole axis, and a mapper registered against a concrete message overrides it. A message that produces no result, and any event, has nothing a mapper could return, so a refusal there always raises.

If the caller should branch on the refusal rather than receive a value of its own, call `TrySendAsync` or `TryQueryAsync` instead. Both return a `MediationResult` carrying the outcome, the reason, the code and any validation failures, so an HTTP boundary produces a 403 or a 400 without catching anything. A genuine fault still throws, which is deliberate: a database timeout is not something a boundary should branch on.

## PipelineContractException Naming More Than One Refusal Mapper

Two mappers producing the same result type are registered at the same level of specificity, so which one applied would depend on assembly scanning order. Remove one, or register the one that should win against the concrete message type, which takes precedence over a mapper registered for a base type.

## An Inbox Message Dead-Lettered Without Retrying

A refusal and a missing handler produce the same outcome on every attempt, so both processors retire such a message on its first attempt rather than spending the retry schedule on an answer that cannot change. Check the dead-letter error text: `LiteBusMessageDeniedException` and `LiteBusMessageInvalidException` are decisions about the message itself, and `NoHandlerFoundException` means nothing is registered to handle it.

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

Behavior. Within a stage, handlers run in ascending `[HandlerPriority]` (default `0`), with registration order breaking ties. Across the onion, global pre-handlers run before specific ones and specific post-handlers run before global ones. The completion stage is the exception: it merges the global and specific sets and orders by priority alone, because the order there decides whether an application's unit-of-work commit runs before or after LiteBus writes its audit record. For events, priority groups and within-group execution follow the concurrency switches on `EventMediationSettings.Execution`; parallel execution makes order non-deterministic.

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
