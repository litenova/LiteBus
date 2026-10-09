# Changelog

All notable changes to this project will be documented in this file.

## v7.0.0-preview.5

What changed since `v7.0.0-preview.4`; for everything landing in 7.0.0, see [v7.0.0](#v700). Acts on a field report from a production .NET 10 application that ported off
its hand-written outbox and domain-event dispatcher onto LiteBus. The theme is honesty about durable messaging: a
dispatch that reached nothing is no longer reported as a success, the operational columns the schema always created
are now written and readable, and three places where the library made a decision the application should have made are
now composition-time choices.

### Added

- `RetryBackoff.Schedule` and `RetryOptions.Delays` take the retry ladder directly. `Fixed` and `Exponential` derive
  every delay from `InitialDelay` under the `MaxDelay` cap, which cannot express a window that starts inside a minute
  and still reaches days: exponential from one minute reaches roughly four hours by the ninth attempt, and stretching
  the tail to a week also pushes the first few retries hours apart, which is the opposite of what a receiver that
  blipped needs. A retry window is a customer-visible guarantee for partner-facing delivery, so it is stated rather
  than approximated. An attempt past the end of the ladder repeats the last entry; `MaxDelay` does not clamp the
  entries, because a cap that silently shortened a declared seven-day tail would defeat the reason for declaring it.
  `RetryOptions.Validate()` runs when the module builds, so an empty or negative ladder fails at boot rather than on
  the first failure, which is the one moment nobody is watching the retry policy.
- `MessageModuleBuilder.UseSerializer<TMessageSerializer>(lifetime)` and `UseSerializerInstance` state the wire format
  of persisted messages at the composition root. The default is `JsonSerializerDefaults.Web`, which is camel case, and
  the only way to change it was to replace the container registration after `AddLiteBus`: one line outside composition
  that no analyzer can see and no startup check can miss. Forgetting it renames every property of every durable
  message on the wire, every consumer breaks, and nothing fails at boot. The builder already owned `UseTimeProvider`,
  `UseAuditTrail` and `UseAuditOutcomeMapper`; the wire format of a stored message is at least as consequential.
- `IdempotencyDeclaration.KeyedByContext()` and `SuppliedIdempotencyKey` cover a key that cannot be projected from the
  message. A processor webhook carries a raw body and a signature and nothing else, deliberately, so that nothing
  unverified reaches a decision; its deduplication key is the provider's event id, which is knowable only after the
  signature is checked and the body parsed. That is a lookup, and a lookup needs services. A guard does the work and
  hands the key forward through `IExecutionContext.Data`, and the pre stage already runs guards before shortcuts. The
  key is read once and reused by both the shortcut that claims it and the completion handler that settles it, so the
  verification happens once per mediation rather than once per stage. It is deliberately not a container-resolved
  provider: each stage resolves the key independently, so a provider would pay the cost twice and, if it were not
  perfectly deterministic, write a different key at each stage. A message declared this way that reaches the shortcut
  with no key raises `AuditConfigurationException` naming the message type, because a generated fallback would make
  every delivery look distinct and the declaration would appear to work while doing nothing.
- `LiteBus.Commands.Recurring` sends one command on a fixed interval for the life of the host, registered through
  `CommandModuleBuilder.AddRecurring`. It replaces the hosted service most CQRS applications write once per background
  job: create a scope, resolve the mediator, send one command, sleep, back off on failure, and never let an exception
  escape the loop. A result implementing `IRecurringWorkReport` can report that work remains, which skips the interval
  and turns a large backlog into one busy period instead of one batch per interval. The package is explicit about what
  it is not: there is no cron expression, no missed-run catch-up, and no leader election, so the loop runs on every
  host instance concurrently. That suits a command that drains work by leasing rows, where the store arbitrates; work
  that must be claimed exactly once belongs on the durable axes, which lease and fence per message.
- `LB1022` warns when `[HandlerPriority]` falls inside the range LiteBus reserves for its own handlers. Reaching for
  `HandlerPriorities.Persistence` to order two application handlers against each other compiles and runs, and produces
  an ordering against the library's commit rather than against the sibling it was meant to be ordered against. The
  bands were already documented; they were not enforceable.
- `InProcessOutboxDispatchOptions.RequireHandler`, configured through `UseInProcessDispatch(dispatch => ...)`,
  controls whether a dispatched message with no handler fails the attempt. It defaults to `true`; see **Changed**.
- `CommandModuleBuilder.RegisterChildModule` is the seam feature packages compose through, so a capability layered on
  the command axis is registered inside `AddCommands` rather than as a second top-level module the caller has to
  remember to add. `CommandModule` is now an `ICompositeModule`.

### Changed

- Outbox in-process dispatch requires a handler by default. `EventMediationSettings.ThrowIfNoHandlerFound` defaults to
  `false`, and `EventOutboxDispatcher` used it as-is, so an event with no registered handler was leased, dispatched
  and marked `Published` without ever leaving the process. If the assembly holding the handlers is not scanned by
  `AddEvents`, that is total data loss with no exception, no dead letter, and a backlog reading of zero. A silent
  no-op is a defensible default for an immediate publish, where the caller can see there are no subscribers; a durable
  dispatch has no such caller, and the whole promise of the outbox is that a committed message reached somewhere. The
  inbox axis could never have this failure, because command mediation requires exactly one handler, so the two durable
  axes disagreed about whether a dispatch with nowhere to go was an error. Set `RequireHandler = false` to restore the
  old behavior where publishing to zero handlers is deliberate.
- The operational columns are written and readable. `last_attempted_at`, `first_failed_at`, `dead_lettered_at`,
  `last_lease_owner` and `error_type` were created by the DDL, validated by the schema check at boot, and mapped by
  the EF Core model, and four of the five were written by nothing and read by nothing. An operator dashboard querying
  them read nulls forever. They are now on `InboxEnvelope` and `OutboxEnvelope`, written by every store adapter on
  every terminal transition, and returned by `IInboxManager.QueryAsync` and `IOutboxManager.QueryAsync`, so a
  dead-letter listing no longer needs a second statement against the library's own table. `FirstFailedAt` is set once
  and is not moved by later failures, so it keeps answering how long a message struggled; `AsRequeued` clears the
  failure record and keeps the attempt history.
- The event axis agrees on one message constraint. `IEventGuard<TEvent>`, `IEventValidator<TEvent>`,
  `IEventShortcut<TEvent>` and `IEventCompletionHandler<TEvent>` constrained `TEvent : IEvent` while
  `IEventHandler<TEvent>` and the pre, post and error roles constrained `notnull`. There was no principle that made a
  guard need the marker interface when the handler it guards did not, so a domain event with no LiteBus marker could
  be handled and nothing else. All four are now `notnull`, and `EventModuleBuilder.RegisterFromAssembly` also
  registers an `IMessageDefinition` declared over an unmarked event, so a POCO event can carry the audit or
  idempotency metadata that belongs beside its handler.

### Fixed

- The `LB1014` dispatcher detection missed an extension method that takes an optional argument. It read
  `Parameters[0]` of the reduced symbol before falling back to `ReducedFrom`, so the check only recognized extensions
  that take nothing beyond the builder. Adding a configuration parameter to any `Use*Dispatch` extension would have
  reported a correctly configured processor as missing its dispatcher.
- The Marten path is tested and the snippet documenting it is correct. `PostgreSqlMartenTransactionalOutboxTests`
  asserts that a Marten document and an outbox row commit together, that neither survives a rollback, and that the
  writer refuses to resolve with no active transaction. The guide's snippet assigned `SessionOptions.Connection` and
  `Transaction`, which are read-only in current Marten; it now uses `SessionOptions.ForTransaction`. The
  `EnableAmbientTransactionProvider` remarks and the `RequireActiveTransaction` exception message now say that another
  ORM joins the transaction rather than owning one, because both are where someone concluding the path does not exist
  actually looks. A consumer reading the docs concluded exactly that and hand-wrote inserts against the outbox table,
  whose schema LiteBus creates, versions and validates.

### Breaking

- `ParallelFaultMode` is now `HandlerFaultMode`, `AggregateAll` is now `ContinueAndAggregate`, and
  `EventExecutionSettings.ParallelFaultMode` is now `FaultMode`. The old name described a mechanism when the thing it
  configures is a policy: whether a failing handler suppresses its siblings. It now applies to sequential execution as
  well, which is the substantive change. Under sequential broadcast the first handler that threw stopped every handler
  behind it, and the only escape was turning on parallel execution, which forces thread-safety onto handlers that
  share a resource such as an ORM session. Fault isolation no longer costs a move to `Parallel`. The mode governs
  handlers inside one priority group and never carries across groups, because a priority difference is the explicit
  statement that the later group runs after the earlier one.
- `AsFailed` and `AsDeadLettered` on both envelopes take a `MessageFailure` instead of a string. The three parts of a
  failure answer three different questions: the formatted text is what a person reads, the exception type is what a
  dashboard groups by, and the timestamp is what fills `first_failed_at` and `dead_lettered_at`. Storing only the
  formatted string forced every consumer to parse it back apart. Use `MessageFailure.From(exception, occurredAt)`, or
  `MessageFailure.FromError(text, occurredAt)` for a failure with a reason but no exception.
- `IdempotencyDeclaration.KeySelector` is nullable. It is `null` for a declaration created by `KeyedByContext()`,
  whose key comes from the execution context rather than from the message.
- `CommandModuleBuilder` no longer takes an `IMessageRegistry` and an `IContractWriter`, and queues registrations
  instead of writing them straight through. `CommandModule` is an `ICompositeModule`, so its configuration action runs
  during `DeclareChildren`, before the live registry exists. Applications are unaffected; code that constructed the
  builder directly is not.

## v7.0.0-preview.4

What changed since `v7.0.0-preview.3`; for everything landing in 7.0.0, see [v7.0.0](#v700). Acts on the second round of feedback from the application migrating onto the v7
previews. The theme is ownership: an application can now define its own audit record shape, and two checks that could
not be satisfied as shipped no longer have to be turned off to get a build through.

### Added

- An application can own the audit record shape. `AuditingBuilder.UseRecordWriter<TWriter>(lifetime)` and
  `UseRecordWriterInstance`, over `MessageModuleBuilder.UseAuditRecordWriter`, replace the writer that turns a
  completed mediation into a record. `AuditRecord` was always a handoff to `IAuditTrail` rather than a persistence
  schema, so a different set of fields needs no new abstraction: it needs a different `IAuditRecordWriter`, which is
  the entire contract between the pipeline and auditing. A replacement keeps the completion-stage placement, the
  `Observability` priority and the per-axis selection, and replaces exactly the record building. Everything the
  built-in writer uses to do that was already public: the audit position through `IMessageRegistry`,
  `AuditReasonMissingException`, `IAuditScope`, `AmbientExecutionContext`, and `IAuditOutcomeMapper`. Skipping a
  message that declares no audit position is part of the contract rather than a detail, because without it every
  message on the selected axes produces a record. There is deliberately no generic `AuditRecord<T>` or
  `IAuditTrail<TRecord>`: a type parameter there spreads through the writer, three axis completion handlers, the
  probe, the builder and `AddAuditing`, so every application wanting the default would pay for the few that do not,
  and it buys nothing a writer cannot already do.
- `LiteBusCompositionSummary.AuditRecordWriter` names a writer that replaced the built-in one, and the startup line
  reports it. Replacing the writer replaces the whole of record building and nothing else in the composition would
  say so, which makes a misregistration look exactly like auditing working.

### Changed

- The `litebus.audit.trail` probe no longer asserts an `IAuditTrail` is registered when the record writer was
  replaced. It reads `LiteBusCompositionSummary.AuditRecordWriter` and reports `Healthy` with the writer instead: a
  writer LiteBus did not build need not use a trail or an actor resolver at all, so demanding either reported a
  correct configuration as unhealthy. It reads the summary rather than resolving `IAuditRecordWriter`, because
  resolving the built-in writer with no trail throws, which is the state the probe exists to report.

### Fixed

- `RequireExplicitOpenGenerics` was unsatisfiable for an open generic living in a scanned assembly, which is every
  handler worth sharing. An explicit `Register(typeof(MyGuard<>))` did not clear the scanned mark in either order,
  and `RegisterFromAssembly` re-marked any open generic in the assembly it walked, so the only way to satisfy the
  check was to move the handler into an assembly nothing scans. The exception even named a fix that did not work. The
  registry now records the two origins separately and `IMessageReader.ScannedOpenGenericHandlers` subtracts them, so a
  registration line clears the mark whichever order the two arrive in, and clearing it does not cost the closure.
- `LB1018` and `LB1020` could not see `IMessageDefinition<TMessage>.Describe`, the shape the documentation calls the
  one to reach for. Both share `DeclarationAnalysis`, which looked only for the attributes and the keyed
  `IMessageDefinition<TMessage, TValue>`, so a message declared the recommended way was reported as declaring nothing
  and the only remedy was to turn the rule off. The pass now reads the `Describe` body: `Declare<TValue>` and
  `Exempt<TValue>` match on the type argument, and `Audited(...)` and `NotAudited(...)` both count as declaring
  `AuditDeclaration`, because each states an audit position and the rule asks whether the message answered rather
  than which answer it gave. A body that cannot be read, one in a referenced assembly or one that hands the collector
  to another method, is treated as declared: a false positive is a build that cannot be made to pass, while a false
  negative leaves a rule `RequireDeclaration` still enforces at startup.

### Breaking

- `AuditActor.Kind` is required, and `AuditActor.User` and `For` no longer take a display name. An actor of no kind is
  a state no audit query can use and no factory produced, so requiring it removes a fourth answer nobody wanted.
  `DisplayName` is documented as the field that makes the trail hold personal data and that LiteBus deliberately does
  not populate, so putting it behind an optional second argument made the easiest thing at a call site the thing the
  documentation warns about. Write `AuditActor.User(id) with { DisplayName = name }`, which is the same amount of code
  and a different amount of attention. `Kind` stays a string, because the set of things that can act belongs to the
  application, and an application whose set is closed keeps it closed where it constructs the actor.

### Documentation

- A preview after the first now publishes only what changed since the preview before it, which is why these notes are
  a delta rather than the whole v7.0.0 section. The first preview of a line and its stable release still publish that
  section in full. `AGENTS.md` and `CONTRIBUTING.md` record the convention.

## v7.0.0-preview.3

What changed since `v7.0.0-preview.2`; for everything landing in 7.0.0, see [v7.0.0](#v700). This preview acts on the feedback from the first application to migrate onto
the v7 previews. Most of it is about the distance between what the pipeline could already express and what an
application still had to write by hand to use it: the audit trail knew what happened but not who did it, a refusal
could only be caught rather than received, a declaration had to be repeated on every message it applied to, and
nothing could report what the composition had actually assembled.

### Added

- Actor attribution on the audit trail. `AuditActor`, `IAuditActorResolver`, and `AuditRecord.Actor` record who
  performed an audited action, which is the field an audit trail is least useful without.
- `MessageModuleBuilder.AddAuditing` configures the trail, the actor resolver, the outcome mapper, and which axes are
  audited in one place, in place of a sequence of separate registrations.
- Events are audited. An event now records alongside commands and queries instead of being excluded by construction.
- `IMessageDefinition<TMessage>.Describe(IMessageDeclarations)` declares several values for one message from a single
  definition class, rather than one class per declared value.
- Scoped declaration requirements. `RequireDeclaration<TValue, TScope>()` and the predicate overload require a value
  only of the messages a marker or a predicate covers, so one family can be held to a rule the rest is not.
- Family defaults. `DeclareDefault<TScope, TValue>(value)` and `IMessageWriter.AddDeclaration(MessageDeclarationItem)`
  declare a value once for a whole family; a message carrying its own still keeps it.
- Composition checks. `ValidateComposition(Action<IMessageCatalog>)`, `RequireUniqueAuditActions`,
  `RequireAuditActionFormat`, and `RequireExplicitOpenGenerics` fail the build rather than a request in production.
- `ICommandMediator.TrySendAsync`, `IQueryMediator.TryQueryAsync`, and `EvaluateAsync` on both. A refusal arrives as a
  `MediationResult` instead of an exception, and `EvaluateAsync` asks whether a message would be permitted without
  performing it, which is what a UI needs to decide whether to offer the action at all.
- Mediation telemetry. An `ActivitySource` and a `Meter` named `LiteBus.Mediation`, `MediationTelemetryOptions` to
  choose what is emitted, and a new `LiteBus.Messaging.Extensions.OpenTelemetry` package registering both with a host.
- `IMessageCatalog.Explain(Type)` reports the pipeline a message will actually run as a `MessagePipelinePlan`, and
  `LiteBusCompositionSummary` reports what the composition assembled.
- `MediationHarness` in `LiteBus.Testing.Mediation` runs the shipped pipeline over hand-supplied handler instances, so
  a pipeline test does not need a container.
- The audit catalogue. `AuditCatalogue.ToRows` and `ToMarkdown` derive it from the declarations, and `IMessageCatalog`
  is registered as a Singleton so it resolves at runtime and not only inside a composition check.
- Keyed data on `IExecutionContext.Data`, a `Code` on `Shortcut` and on completion, and `AuditReasonMissingException`.
- Typed configuration exceptions: `ModuleCompositionException`, `MessageDeclarationException`,
  `PipelineContractException`, `DurableStorageConfigurationException`, and `AuditConfigurationException`.

### Changed

- `LiteBusConfigurationException` is no longer sealed, because the typed exceptions above derive from it. A `catch` on
  the base still catches every one of them.
- The axis builders accept `IMessageDefinition<>` and the messaging-level handler contracts, so a definition or a guard
  written against the messaging abstractions registers on the axis whose message type it constrains.
- `IExecutionContext` is registered as Scoped, so a handler can take it as a dependency instead of reaching for the
  ambient accessor.

- `AuditActor.Kind` is required, and `AuditActor.User` and `For` no longer take a display name. An actor of no kind
  is a state no audit query can use and no factory produced, so requiring it removes a fourth answer nobody wanted.
  `DisplayName` is documented as the field that makes the trail hold personal data and that LiteBus deliberately does
  not populate, so putting it behind an optional second argument made the easiest thing at a call site the thing the
  documentation warns about; it is now `AuditActor.User(id) with { DisplayName = name }`, which is the same amount of
  code and a different amount of attention. `Kind` stays a string, because the set of things that can act belongs to
  the application, and an application whose set is closed keeps it closed where it constructs the actor.
- The `litebus.audit.trail` probe no longer asserts an `IAuditTrail` is registered when the record writer was
  replaced. It reads `LiteBusCompositionSummary.AuditRecordWriter` and reports `Healthy` with the writer instead: a
  writer LiteBus did not build need not use a trail or an actor resolver at all, so demanding either reported a
  correct configuration as unhealthy. It reads the summary rather than resolving `IAuditRecordWriter` because
  resolving the built-in writer with no trail throws, which is the state the probe exists to report.

### Fixed

- `RequireExplicitOpenGenerics` was unsatisfiable for an open generic living in a scanned assembly, which is every
  handler worth sharing. An explicit `Register(typeof(MyGuard<>))` did not clear the scanned mark in either order, and
  `RegisterFromAssembly` re-marked any open generic in the assembly it walked, so the only way to satisfy the check
  was to move the handler into an assembly nothing scans. The exception even named a fix that did not work. The
  registry now records the two origins separately and `IMessageReader.ScannedOpenGenericHandlers` subtracts them, so a
  registration line clears the mark whichever order the two arrive in, and clearing it does not cost the closure.
- `LB1018` and `LB1020` could not see `IMessageDefinition<TMessage>.Describe`, the shape the documentation calls the
  one to reach for. Both share `DeclarationAnalysis`, which looked only for the attributes and the keyed
  `IMessageDefinition<TMessage, TValue>`, so every message declared the recommended way was reported as declaring
  nothing, and the only remedy was to turn the rule off. The pass now reads the `Describe` body: `Declare<TValue>` and
  `Exempt<TValue>` match on the type argument, and `Audited(...)` and `NotAudited(...)` both count as declaring
  `AuditDeclaration`, because each states an audit position and the rule asks whether the message answered rather than
  which answer it gave. A body that cannot be read, one in a referenced assembly or one that hands the collector to
  another method, is treated as declared, because a false positive is a build that cannot be made to pass while a
  false negative leaves a rule `RequireDeclaration` still enforces at startup.
- The events strategy dropped `decision.Code`, so an event denial reported no code.
- `Describe` is invoked reflectively, which wrapped a configuration error in `TargetInvocationException`.
- `EventModule` registered the audit completion handler after its handler scan, so the type never reached the container
  and no event was ever audited.
- `[AuditExempt]` accepted an empty rationale, which is the one thing the attribute exists to record.

### Breaking

- `HandlerPriorities.UnitOfWork` moved to `ReservedCeiling + 100`. An application's unit-of-work commit now runs after
  the framework's own writers rather than between them, which is the order the inbox and the audit trail need to be
  committed in the same transaction as the work.
- `ICommandMediator` and `IQueryMediator` gained members without default implementations, so a hand-written
  implementation of either, such as a test double, has to implement them.

### Documentation

- Arity-2 open generic handlers, `IMessageShortcut<,>` and `IMessageRefusalMapper<,>`, were already supported in
  preview.2 but undocumented. They are now recorded and covered by regression tests.
- The dispatch scope is created from the root scope factory, making it a sibling of the request scope rather than a
  child. `hosted-services.md` described a "per-request `DbContext`", which is the opposite of what happens.
- `IExecutionContext.Data` was missing from the execution context listings, and no page said what the Post stage is
  for.

## v7.0.0

Major release for .NET 10, describing the change from v6.0.2. Adds a completion stage to the mediation pipeline, four
named decision stages that decide whether work happens, declarative message metadata, and an audit trail and in-process
idempotency built on all three. The pre stage is where most of the break lands: guards, validators, shortcuts, and
pre-handlers are now four contracts the framework can tell apart before invoking them, which is what lets it fix the
order they run in. The declaration model is general rather than an auditing feature: an application declares its own
metadata, reads it back through an accessor, enforces it at compile time and at composition time, and a named priority
band gives its unit-of-work commit a position the framework's own writers can be ordered against. In-process mediation
becomes observable, a refusal can be received as a value rather than caught, and a message can be asked whether it
would be permitted without being performed. Persistence schemas and transport behavior are unchanged.

### Added

- `TransportOutboxDispatcherOptions.TenantRoutingStrategy` and
  `OutboxModuleBuilder.UseContractResolver(IMessageContractResolver)` make the dispatcher's tenant routing and the
  envelope factory's contract resolver configurable. Both constructor parameters existed before, but no builder could
  fill them.
- `TransportPublishRequest.MessageGroupId` and `TransportPublishRequest.DeduplicationKey`. The transport outbox
  dispatcher fills them from the envelope (the idempotency key, otherwise the message id; the tenant id, otherwise the
  contract name), and the SQS adapter sets them on queues whose URL ends in `.fifo`, so outbox dispatch can publish to
  SQS FIFO queues.
- `KafkaTransportOptions.SocketTimeoutMs` and `KafkaTransportOptions.MessageSendMaxRetries`, each positive when set and
  left at the librdkafka default otherwise.
- `AmqpReceivedMessage.QueueName`, and `AmqpConsumerOptions.MaxInFlightMessages` so a typed `IAmqpConsumer` subscription
  gets the same admission control as the generic consumer path.
- `DiagnosticCheckRunOptions.RunTimeout`, an overall budget for one diagnostic run. Unset, it is the worst case of the
  probe count packed into `MaxParallelism` lanes at `Timeout` each; probes still running when it expires are cancelled
  and reported unhealthy.
- `AmbiguousMessageResolveException.Candidates` names the registered types that tied, and
  `IdempotencyConfigurationException` reports an idempotency declaration mistake.
- `SagaPurgeFilter.Take` bounds a saga purge.
- A [Limits and Opt-in Boundaries](site/content/docs/reference/limits.md) page, and a documentation home that states
  what LiteBus costs and which page answers which question.
- `ILeaseRenewable.ReleaseLeaseAsync(LeaseReleaseRequest, CancellationToken)` returns a leased message to the queue
  without charging the attempt its lease consumed. The pipelined processor calls it for every dispatch it cancelled
  during shutdown or a drain, so a host restart no longer moves `attempt_count`. Implemented by the in-memory,
  PostgreSQL and EF Core inbox and outbox stores; the default implementation releases nothing, so a custom store still
  compiles and falls back to lease expiry. `OutboxEnvelope.AsReleased` and `InboxEnvelope.AsReleased` are the matching
  transitions. EventIds 3011 (Debug) and 3012 (Warning, release failed) record the outcome.
- `MediationExceptionFilters.IsCallerCancellation` recognizes the caller's cancellation even when a dispatcher wrapped it
  in its own exception type.
- `RecurringCommandHostOptions.IsConcurrencyConflict`, defaulting to `IsConcurrencyConflictByTypeName`. A send that
  loses an optimistic-concurrency race to another instance is logged at Information (EventId 2104) and the loop waits
  `Interval` rather than `ErrorBackoff`, because the work it wanted is already done.
- `InMemoryTransportBroker` implements `IDisposable` and completes its destination channels.
- Two operator pages: [Message States and Alerting](site/content/docs/operations/message-states.md) and
  [Ordering and Concurrency Guarantees](site/content/docs/reliable-messaging/ordering.md), and two roadmap design notes
  from an integrator field report: a Marten transactional outbox writer, and analyzer and API gaps.

- A fifth pipeline stage. `IMessageCompletionHandler<TMessage>`, `IMessageCompletionHandler<TMessage, TMessageResult>`,
  and the axis contracts `ICommandCompletionHandler`, `IQueryCompletionHandler`, and `IEventCompletionHandler` run in a
  `finally` on every mediation path, exactly once, and receive a read-only `MessageCompletionContext` carrying the
  outcome, the result, the exception, the reason, and the elapsed duration. Post-handlers run only when the main handler
  succeeds and error handlers only for recoverable exceptions, so until now no stage could observe how a message
  actually ended. Recording an audit entry, emitting a metric, or closing a unit of work belongs here.
- The completion stage is not cancellable. Handlers receive `CancellationToken.None`, because the ending has already
  happened and handing the stage the token that just fired would drop exactly the records a review looks for.
- Guards. A pre-stage handler that may refuse a message implements `IMessageGuard<TMessage>` and returns a `Verdict`
  from `DecideAsync`, with the axis contracts `ICommandGuard<TCommand>`, `IQueryGuard<TQuery>`, and
  `IEventGuard<TEvent>`. A refusal always carries a reason, may carry a code, and reports `MediationOutcome.Denied`, which
  an audit trail records as a denial. The compiler requires the decision, so nothing after it runs by accident, and an
  expected control-flow path stays off the exception path.
- Validators. `IMessageValidator<TMessage>` returns `Validity` from `ValidateAsync`, with the axis contracts
  `ICommandValidator<TCommand>`, `IQueryValidator<TQuery>`, and `IEventValidator<TEvent>`. A validator answers whether
  the message is well-formed, which is a different question from whether the caller may send it, so a failure reports
  `MediationOutcome.Invalid` rather than `Denied` and stays out of the list a security review reads. Unlike every other
  decision stage, this one runs every validator and collects their failures rather than stopping at the first: a caller
  fixing a malformed message should not discover its problems one round trip at a time. `ValidationFailure` carries the
  message, the member it applies to, and an optional code.
- Refusal mappers. `IMessageRefusalMapper<TMessage, TMessageResult>` turns a guard refusal or a validation failure into
  the value the caller receives, for applications that model failure as data rather than as an exception, with the axis
  contracts `ICommandRefusalMapper`, `IQueryRefusalMapper`, and `IStreamQueryRefusalMapper`. One registration against
  `ICommand` covers every command producing that result type, and a mapper registered against a concrete message wins
  over it. Without a mapper, a refusal reaches the caller as `LiteBusMessageDeniedException` or
  `LiteBusMessageInvalidException`.
- Shortcuts. A pre-stage handler that answers a message whose work is already done implements
  `IMessageShortcut<TMessage>` or `IMessageShortcut<TMessage, TMessageResult>` and returns a `Shortcut` from
  `TryAnswerAsync`, with the axis contracts `ICommandShortcut<TCommand>`, `ICommandShortcut<TCommand, TCommandResult>`,
  `IQueryShortcut<TQuery, TQueryResult>`, `IStreamQueryShortcut<TQuery, TQueryResult>`, and `IEventShortcut<TEvent>`. A
  cache hit or a replayed idempotent command reports `MediationOutcome.Answered`, which an audit trail records as a
  success because nothing was denied. Keeping that apart from a denial is the distinction a security review reads.
- The framework fixes the stage order: guards, then validators, then shortcuts, then pre-handlers. Priority orders
  handlers inside a stage and never reorders the stages, so a globally registered cache shortcut cannot answer a caller
  that a message-specific authorization guard would have denied, and a malformed message cannot claim an idempotency
  key. The order encodes what each stage may assume about its input: a guard sees every message, a validator sees only
  messages the caller is allowed to send, a shortcut sees only well-formed ones, and a pre-handler sees only messages
  that are going to be handled. Under a single pre-handler stage that ordering rested on a priority number the author
  had to remember, and indirect handlers ran ahead of direct ones regardless. ASP.NET Core documents the same hazard for
  `UseOutputCache` after `UseAuthorization`; because LiteBus owns its stages, it makes the mistake unrepresentable
  instead of documenting it. `PreStage` names the four stages and `IPreStageHandlerDescriptor.Stage` records which one
  runs a handler.
- `Shortcut<TMessageResult>` types the answer over the result type of the message, so a shortcut that answers a
  result-returning message is required by the compiler to supply the value the caller receives. Answering always carries
  the result; a stream query that means no items answers with `AsyncEnumerable.Empty<T>()`, which states that outright
  rather than leaving it implied by a missing value. A denial owes the caller nothing, so one guard contract fits every
  message, and the value a denied caller receives comes from a refusal mapper instead.
- `MessageContextExtensions.RunAsyncPreStages` gives a custom mediation strategy the same stage order the shipped
  strategies use, in one call rather than four, because running one stage without the others cannot honor the ordering
  guarantee the split exists to provide. `ResolveRefusalResult<TMessageResult>` applies the registered refusal mapper,
  or raises when none covers the message. Both live in `LiteBus.Messaging` rather than in the abstractions package.
- `RunAsyncErrorHandlers` and `RunAsyncCompletionHandlers` each take the execution context and open their own ambient
  scope, so a strategy no longer has to wrap them. The error runner also captures the `ExceptionDispatchInfo` itself,
  which is what preserves the original stack when nothing recovers, and the completion runner resolves a post-handler's
  replacement result in preference to the handler's own. Both rules used to be a strategy's job to remember. The
  completion runner takes the outcome, the failure, and the reason a strategy tracked, and builds the context from them.
- `LiteBusMessageDeniedException` and `LiteBusMessageInvalidException` reach the caller when no refusal mapper covers
  the message. Both are excluded from the recoverable-exception filter, so error handlers never see a decision as a
  fault, and `LiteBusMessageInvalidException.Failures` carries every failure the validator stage collected.
- `MediationExceptionFilters.IsRefusal` and `IsRetryableDispatchException` classify a decision apart from a fault. The
  inbox and outbox processors use the second to dead-letter a refusal or a missing handler on the first attempt instead
  of spending the retry schedule on an answer that cannot change.
- `MediationOutcome` distinguishes `Succeeded`, `Answered`, `Denied`, `Invalid`, `Failed`, and `Canceled`. Every member
  is reported by some path, and each names a state the message ended in rather than a mechanism the pipeline used.
- `IExecutionContext.SuppressPostHandlers()` skips the post-handlers that have not run yet. Use it when the work turned
  out to be a no-op and the reactions to it should not fire, such as an idempotent command that detects it already ran.
  It does not stop the calling handler and does not change the outcome.
- Declarative message metadata. `IMessageDescriptor.Metadata` exposes values resolved once at registration from
  declaring attributes on the message type and from message definitions, so a pipeline stage reads a dictionary instead
  of reflecting on every dispatch.
- Message definitions. A definition class lives beside the message it describes and declares one value per concern
  through `IMessageDefinition<TMessage, TValue>`. Declarations are keyed by value type, so one class may declare several
  without ambiguity, and applications may declare their own value types that LiteBus applies without interpreting. A
  declaration covers the message type it names and every message assignable to it, so one definition can describe a
  family of messages; the most derived declaration wins.
- `IMessageDeclarationSource` marks an attribute as a source of message metadata and states the value type it declares.
  Only attributes implementing it are collected, which keeps metadata bounded, and it puts attributes and definitions on
  one key so a definition genuinely overwrites an attribute rather than sitting beside it.
- An audit trail at the mediation boundary. `[Audited]` and `[AuditExempt]`, or an `IAuditDefinition<TMessage>`, declare
  the constant half of a record; `IAuditScope` supplies what only the handler knows. `EnableAuditing()` on the command
  and query module builders registers the writer, which hands an `AuditRecord` to the application's `IAuditTrail`.
  Because it runs at the completion stage, refusals, failures, and cancellations are recorded as first-class outcomes.
- The trail itself is registered on the messaging module through `UseAuditTrail<T>()` or `UseAuditTrail(instance)`,
  beside the outcome mapper, so the shared half of auditing is configured in one place while the per-axis switch
  stays where the decision belongs.
- `AuditDeclaration` is a closed hierarchy of `AuditedDeclaration` and `AuditExemptDeclaration`, so a declaration cannot
  hold a combination that means nothing, such as a category on an exemption.
- `ReasonRequired` on an audited declaration is enforced. A successful action that declares it and supplies no reason
  raises `LiteBusConfigurationException` rather than writing an incomplete record.
- `AuditTrailDiagnosticCheck` reports the `litebus.audit.trail` probe as unhealthy when auditing is enabled and no
  `IAuditTrail` is registered, so a missing sink surfaces before the first audited mediation.
- `IAuditOutcomeMapper` and `MessageModuleBuilder.UseAuditOutcomeMapper` let an application that refuses by throwing
  record its own exception as `AuditOutcome.Denied` rather than `AuditOutcome.Failed`. Refusing through a guard needs no
  mapper.
- `MediationExceptionData.SuppressedCompletionFaults` is the key under which a completion-handler fault is attached to
  the exception that was already ending the mediation, so a failed audit write is never silently discarded.
- `LB1018` reports command and query types that state no audit position, so an unaudited message is a recorded decision
  rather than an oversight. Disabled by default; enable with `dotnet_diagnostic.LB1018.severity = warning`.
- `LB1019` reports a shortcut that implements the untyped shortcut contract for a message that produces a result.
  Because `ICommand<TResult>` derives from `ICommand`, that contract compiles there, and answering from it fails at
  runtime with `LiteBusConfigurationException`. The typed contract is a strict superset for such a message, so the rule
  names it and the declaration is where the fix goes. Open generic shortcuts are not reported, and guards and validators
  never are: a refusal owes the caller no result, so one contract is correct for every message on those stages.
- Registration rejects an untyped shortcut declared for a message that produces a result, so the mistake `LB1019`
  reports cannot reach production in a project that does not reference the analyzer package. The check runs from
  both directions, since a handler may be registered before or after the message it handles.
- `HandlerPriorities` reserves a priority window for handlers shipped by LiteBus, so ordering against them is a
  documented guarantee. `ReservedFloor` opens it, `ReservedCeiling` closes it, and application handlers own the band
  below the floor, where an unannotated handler already sits, and the band at or above the ceiling. `UnitOfWork` names
  the position in that upper band where an application commits, which is what makes an audit record staged by the
  writer at `Observability` part of the transaction that applies the change it describes. Only `Persistence` and
  `Observability` may be reordered between releases, and only against each other.
- `IExecutionContext.Data` is an `IHandleContextData`: a store keyed by the CLR type of the value rather than by a
  string, created once per mediation. It exists so a guard whose decision depends on loaded state can hand the loaded
  object to the main handler instead of forcing a second round trip, which is the cost that keeps authorization inside
  handlers rather than in the stage that owns the decision. `Items` stays for string-keyed interop. `Get<T>` throws
  `HandleContextDataNotFoundException` naming the type, `TryGet<T>` covers the optional case, and access is
  lock-guarded because parallel event handlers share one execution context.
- `IMessageMetadataAccessor` reads a message type's declared metadata from application code, through `ForMessage` and
  `TryGet`. Reading a declaration previously meant injecting `IMessageRegistry`, calling `Find`, and reaching through
  `IMessageDescriptor.Metadata`, which made the registry's descriptor shape part of every application that wanted to
  read a declaration it wrote itself. An unregistered type raises `MessageMetadataNotFoundException` rather than
  answering with an empty collection, because an empty answer turns a missing registration into a permission check that
  silently passes.
- `MessageModuleBuilder.RequireDeclaration<TValue>()` fails composition for any registered message that neither
  declares a value of that type nor records an exemption from it, naming every offender grouped by the declaration each
  one omits. It runs through the new `IModuleConfiguration.RegisterCompositionValidation` hook, after every module has
  built, because the messaging module is foundational and has no commands to inspect during its own build.
- `[DeclarationExempt(typeof(TValue), rationale)]` records that a message deliberately declares nothing for one
  metadata type. It is repeatable and every instance is aggregated into one `DeclarationExemptions` value, readable
  through the accessor like any other declaration. The rationale is what separates a decision from an omission.
- `[MessageDeclaration(typeof(TValue))]` states, on an attribute class, which metadata value that attribute declares.
  `IMessageDeclarationSource.DeclarationType` is a runtime property an analyzer cannot execute, so without a static
  declaration no compile-time rule can tell that `[RequiresPermission]` is how a message states its permission.
  Registration fails when the annotation and the property name different types.
- `LB1020` reports a command or query type that states no position on a metadata value type named in
  `litebus_required_declarations`, and `LB1021` reports a configured name that does not resolve. `LB1018` is now the
  preconfigured instance of `LB1020` over `AuditDeclaration`, sharing its analysis rather than duplicating it. A
  written policy such as "every command states the permission it requires" becomes a build failure instead of something
  code review has to catch.
- `ThrowingValidator<TMessage, TException>`, with the axis specializations `ThrowingCommandValidator` and
  `ThrowingQueryValidator`, adapts a validator whose body still reports failure by throwing. It is migration
  scaffolding for the `Validity` signature change: adapted and converted validators mix in one mediation because the
  stage collects across both, so a codebase converts module by module instead of in one commit touching every
  validator. Only the named exception type is caught, so a genuine fault inside a validator still ends the mediation as
  a failure.
- Open generic handlers may take two type parameters, binding the message type and the result type the message declares
  through the new `IProducesResult<TMessageResult>` marker on `ICommand<T>`, `IQuery<T>` and `IStreamQuery<T>`. A
  generic post-handler, completion handler, or error handler reaches the typed contract instead of falling back to an
  `object?` it can do nothing with. Arity 2 is accepted only when the handler implements a contract taking both
  parameters in order, so a second parameter the handler invented is still rejected, and a message declaring no result
  is skipped the way a constraint mismatch is.
- In-process idempotency. `IIdempotencyDefinition<TMessage>` declares the key a repeat is recognised by,
  `IIdempotencyStore` remembers which keys were applied, and `CommandModuleBuilder.EnableIdempotency()` registers two
  shortcuts and a completion handler that claim the key before the handler runs and settle it after: applied on
  success, released on anything else, so a transient failure does not turn the retry into a false repeat. The durable
  inbox and outbox already deduped envelopes; this is the same problem one layer in, where the shortcut stage was
  already the right shape and only the declaration and the storage contract were missing.
  `IdempotencyDeclaration.ReplayResult` records the first answer so a repeated result-producing command can be answered
  with it; without it, a repeat raises a configuration error naming the fix rather than inventing a `default`.
  `InMemoryIdempotencyStore` ships from `LiteBus.Testing.Mediation`, because a store that forgets on restart and knows
  only its own process cannot make a claim about the system.
- The `litebus.idempotency.store` diagnostic probe reports `Unhealthy` when idempotency is enabled and no
  `IIdempotencyStore` is registered, and the `litebus.audit.trail` probe now also reports `trailIsSingleton`, resolved
  by comparing the trail across two dispatch scopes.
- `IHandlerDescriptor.ContractType` records the closed contract a descriptor was discovered from, and `PipelineDispatch`
  carries the delegate bound to it at registration.
- An audit record says who acted. `AuditRecord.Actor` carries an `AuditActor` with a required `Id` and optional `Kind`,
  `DisplayName` and `OnBehalfOf`, supplied by an `IAuditActorResolver` the application registers through
  `UseAuditActorResolver`. The resolver receives the completion context, so it reads the actor off the message and runs
  on every path, which is what attributes a denied command; a pre-stage handler cannot, because a guard that denies
  stops the pipeline before any pre-handler runs, and that is exactly the case a trail exists for. A handler that knows
  more than the resolver overrides it with `IAuditScope.WithActor`. Returning null is legitimate and means nothing
  established an actor, which is a different answer from a named process. The message itself is deliberately not on the
  record: it is handed to the resolver, so a payload cannot reach audit storage by default. The `litebus.audit.trail`
  probe reports a missing resolver as `Degraded` and carries `actorResolverRegistered`.
- `MessageModuleBuilder.AddAuditing` configures the whole audit feature in one call: the trail, the actor resolver, the
  outcome mapper, and which axes produce records, through `AuditingBuilder`. Before it, the trail lived on the messaging
  builder and the per-axis switch lived on the command and query builders, so an application could register either
  without the other and find out from a diagnostic probe. Configuring a trail and selecting no axis now raises
  `AuditConfigurationException` at composition, which no probe can report at runtime because nothing is ever audited
  and so nothing ever fails. The per-axis `EnableAuditing()` calls remain and are what this composes.
- `EventModuleBuilder.EnableAuditing()`. A domain fact is frequently the thing a review most wants recorded, and the
  event axis had no switch for it while commands and queries did. One record per publish, not per handler: the
  mediation is the unit being audited, and a record per subscriber would turn one fact into as many entries as there
  happen to be reactions.
- `IMessageDefinition<TMessage>` declares every value for a message from one `Describe(IMessageDeclarations)` method,
  with `Declare<TValue>`, `Audited`, `NotAudited` and `Exempt<TValue>` on the collection. The keyed
  `IMessageDefinition<TMessage, TValue>` remains and is the better choice for a single declaration, because the
  compiler checks it; past one it stops paying, since the second and every later value has to be written as an explicit
  interface implementation naming the message type and the value type again, which is the type name three times to say
  one thing. Both shapes write into the same type-keyed bag, so a codebase uses whichever fits each message.
- Declaration requirements can be scoped. `RequireDeclaration<TValue, TScope>()` covers every message assignable to a
  marker, and `RequireDeclaration<TValue>(predicate, description)` covers an arbitrary selection with the words the
  error uses. The unscoped form applied to commands, queries and events alike, so requiring a permission of commands
  also demanded one from every query and produced exemptions that said nothing, which trains a team to treat a
  rationale as paperwork. The scope is a type rather than a namespace on purpose: a namespace is a string a refactoring
  tool moves without telling anyone, and for an authorization rule that failure is an unguarded command that used to be
  guarded. The composition error names the scope alongside every offender.
- `MessageModuleBuilder.ValidateComposition(Action<IMessageCatalog>)` runs an application rule over every registered
  message after every module has built. `IMessageCatalog` enumerates `MessageCatalogEntry` values carrying the message
  type, its resolved metadata, and its `AuditedDeclaration`, with an `Audited()` filter. The underlying hook was public
  and unreachable in practice, because the only way to be handed an `IModuleConfiguration` is to implement `IModule`,
  which is a lot of ceremony for a five-line assertion. `RequireUniqueAuditActions()` and
  `RequireAuditActionFormat(pattern)` ship as built-ins over it, because every audited application needs both and
  getting either wrong corrupts the trail rather than breaking the build.
- `ICommandMediator.TrySendAsync` and `IQueryMediator.TryQueryAsync` return `MediationResult` and
  `MediationResult<TResult>` instead of raising a refusal. A denial and a validation failure are routine endings that
  the pipeline already models as decisions internally, and converting them to exceptions at the boundary left an HTTP
  endpoint catching one to produce a 403. A genuine fault still throws: a database timeout is not something a boundary
  should branch on, and a result carrying the exception would invite one to be swallowed. Where a refusal mapper is
  registered, the mapped value arrives alongside the denied outcome, so an application sees both its own shape and the
  framework's classification.
- `ICommandMediator.EvaluateAsync` and `IQueryMediator.EvaluateAsync` answer whether a message would be permitted and
  well-formed, without performing it, returning a `MediationDecision`. This is what removes the second authorization
  method an application otherwise writes, one to authorize while doing and one for a caller that shows or hides a
  control; two methods answering the same question drift, and the drift is silent and security-relevant, because a
  button stays visible for an action the pipeline will refuse. It runs guards and validators only, because a shortcut
  and a pre-handler act rather than decide: the shipped idempotency shortcut claims a key, so evaluating a page full of
  controls would burn keys for commands nobody submitted. `MessageContextExtensions.RunAsyncDecisionStages` exposes the
  same prefix to a custom mediation strategy.
- In-process mediation is observable. `LiteBusMediationTelemetry` declares the `LiteBus.Mediation` activity source and
  meter with public instrument names: `litebus.mediation.duration`, `litebus.mediation.count`,
  `litebus.mediation.stage.duration` and `litebus.mediation.decisions`. One span per mediation named
  `mediate {MessageType}`, tagged with the message, the outcome and the decision code; the decisions counter is tagged
  with the stage and the deciding handler, which turns "which stage denied this" from a stack trace into a filter. Only
  a `Failed` outcome sets the span error status, because a denial is a decision and colouring every refused request red
  makes a trace view useless for finding the requests that actually broke. The inbox, the outbox, the transport and
  each broker adapter all published instruments while the library's primary job published none.
  `MessageModuleBuilder.UseTelemetry(MediationTelemetryOptions)` decides what is recorded: spans and metrics are on,
  per-stage spans and per-stage metrics are opt-in because mediation volume is orders of magnitude above
  durable-processing volume. The new `LiteBus.Messaging.Extensions.OpenTelemetry` package registers the source and the
  meter with `AddLiteBusMediationInstrumentation()` and `AddLiteBusMediationMetrics()`.
- `IMessageReader.Explain(Type)` returns a `MessagePipelinePlan`: the message, the result type it declares, and every
  handler that will run in the order it will run, as `MessagePipelineStep` records naming the stage, the priority, the
  handler, the contract, and whether it arrived indirectly or from a closed open generic. With a hundred messages, open
  generic guards, an audit writer and a commit, the honest answer to "what runs for this command" was to read the
  registry in a debugger. It reproduces the pipeline's own ordering rules, including that completion orders by priority
  alone across the direct and indirect split, and it is read from the registry rather than computed at compile time
  because the registry is the only thing that knows about closed open generics, base-type registrations and priority
  ties.
- `LiteBusCompositionSummary` reports what the host actually composed: message counts per axis, every open generic
  handler with the number of messages it was closed over, the audit trail and its lifetime, whether an actor resolver
  is registered, the declaration policies enforced, and how many application composition checks run. Resolve it and log
  `ToString()` once at startup. The open generic line is what earns it: adding one file to a scanned assembly inserts a
  pipeline stage into every message it fits, and nothing in the composition code shows it.
- `IMessageWriter.RegisterFromScan(Type)` records that a type arrived through an assembly scan rather than being named,
  surfaced as `IMessageReader.ScannedOpenGenericHandlers` and `IMessageReader.OpenGenericClosures`.
  `MessageModuleBuilder.RequireExplicitOpenGenerics()` then fails composition, naming each scanned open generic and the
  registration line that fixes it. It is opt-in rather than the default because picking up open generic handlers is
  what an assembly scan has meant since v4, and turning that off changes what a scan is rather than fixing a defect.
- `IHandleContextData` gained keyed overloads of `Set`, `Get`, `TryGet`, `Contains` and `Remove`. One value per type
  cannot express a command that names two accounts, which is the identity-map case the store's own worked example
  implies. Keys are compared with `object.Equals`, so an identifier value object is usable directly, and the unkeyed
  slot is a distinct slot rather than a reserved key value, so neither can clear the other.
  `HandleContextDataNotFoundException` now carries the `Key` alongside the `DataType`.
- `Shortcut` and `Shortcut<TMessageResult>` carry a `Code` alongside their reason, and `MessageCompletionContext`
  exposes `Code`. `Code` now means the same thing on all three decision shapes and on `Verdict`: something a later
  stage can switch on, where the reason is prose written for a person. Without it, a completion handler counting why
  messages were answered had to match on English, and a metric could not tell a cache hit from an idempotent replay.
- `AuditReasonMissingException` replaces the configuration exception raised when an audited action declares
  `ReasonRequired` and the handler supplied none. It carries the action and the message type.
- Family defaults. `MessageModuleBuilder.DeclareDefault<TScope, TValue>(value)` declares a value for every message
  assignable to a marker, and `IMessageWriter.AddDeclaration(MessageDeclarationItem)` records a declaration without a
  definition class. "Everything under organizations requires ManageOrganization unless it says otherwise" is a rule
  worth stating once; declaring it on each of a hundred commands states it a hundred times and gives it a hundred
  places to drift. Nothing new decides precedence: a declaration still resolves to the one written closest to the
  message, so a command carrying its own value keeps it and the family inherits the default, which is the rule a
  definition written for a base type has always followed. The scope is a type rather than a namespace for the reason a
  scoped requirement is: a namespace is a string a refactoring tool moves without telling anyone, and for an
  authorization default that failure is a command that used to be guarded and now is not. Two defaults for one scope
  and value type are a configuration error, and the writer's default `AddDeclaration` throws rather than accepting the
  call and dropping it, because a silently dropped default is an unguarded command that looks configured.
- An application can own the audit record shape. `AuditingBuilder.UseRecordWriter<TWriter>(lifetime)` and
  `UseRecordWriterInstance`, over `MessageModuleBuilder.UseAuditRecordWriter`, replace the writer that turns a
  completed mediation into a record. `AuditRecord` is a handoff to `IAuditTrail` rather than a persistence schema, so
  a different set of fields needs no new abstraction: it needs a different `IAuditRecordWriter`, which is the entire
  contract between the pipeline and auditing. A replacement keeps the completion-stage placement, the `Observability`
  priority and the per-axis selection, and replaces exactly the record building; everything the built-in writer uses
  to do that is public, including the audit position through `IMessageRegistry`, `AuditReasonMissingException`,
  `IAuditScope`, `AmbientExecutionContext`, and `IAuditOutcomeMapper`. Skipping a message that declares no audit
  position is part of the contract rather than a detail, because without it every message on the selected axes
  produces a record. There is deliberately no generic `AuditRecord<T>` or `IAuditTrail<TRecord>`: a type parameter
  there spreads through the writer, three axis completion handlers, the probe, the builder and `AddAuditing`, so every
  application wanting the default would pay for the few that do not, and it buys nothing a writer cannot already do,
  while the fixed shape is the part worth having an opinion about because it maps onto NIST SP 800-53 AU-3, PCI DSS
  Requirement 10 and the DMTF CADF event model.
- `LiteBusCompositionSummary.AuditRecordWriter` names a writer that replaced the built-in one, and the startup line
  reports it. Replacing the writer replaces the whole of record building, and nothing else in the composition would
  say so, which makes a misregistration look exactly like auditing working.
- The audit catalogue is derived rather than maintained. `IMessageCatalog` is registered as a Singleton so it resolves
  at runtime and not only inside a composition check, and `AuditCatalogue.ToRows` projects every audited message into
  an `AuditCatalogueRow` carrying the action, the message, the category, the target kind, and whether a reason is
  required. `ToMarkdown` is one formatter over those rows. Rows are the primary surface because what a compliance
  process consumes differs per team, and a library emitting only Markdown would serve one team and obstruct the rest.
  Rows are ordered by action so two runs produce the same document. The other half of an authorization matrix stays
  the application's: a required permission is an application value type, projected from
  `MessageCatalogEntry.Metadata` alongside these rows, because only the application knows what its own declarations
  mean.
- `MediationHarness` in `LiteBus.Testing.Mediation` runs the shipped pipeline over hand-supplied handler instances,
  with no host and no container, and reports which pre stages ran. Asserting that a guard denies previously meant
  booting the whole host, which for an application with a relational store meant a database container for a test about
  one authorization decision. `MediationHarnessResult.StagesRun` is the part no consumer can build, because only the
  stage runner knows it, and when the point of the library is that behavior moved into named stages that is the
  assertion a test of the behavior wants. It runs the real strategies through the real stage runner, so the fixed stage
  order, the validator stage's aggregation, and priority ordering all apply; what it leaves out is composition, so a
  registration a host would reject still has to be asserted against a host. `MessageRegistryFactory.Create()` and
  `MessageMediatorFactory.Create(registry, dispatchScopeFactory)` are the public seams it is built on, both
  `EditorBrowsable(Never)`, for a manual host composing LiteBus without a container.

### Changed

- Transport publishers are singletons in all five adapters, so a dispatched envelope no longer opens and closes its own
  AMQP channel, Azure Service Bus sender link or Kafka producer flush. Consumers are transient in all five, Azure
  Service Bus included, because each instance holds one subscription. The Kafka producer is flushed once, when the
  container is disposed.
- Saga state is compared with the JSON snapshot taken when it was loaded. Reading state and then calling `Complete()`
  now works, a dispatch that only reads state saves nothing, and a handler that changes state in place without
  `SetState` is still persisted.
- The `AfterDispatch` hook runs on the persist token on both the inbox and the outbox, so a shutdown during a saga save
  no longer leaves an executed command or a published event to run again. With the default `HonorShutdownTokenOnPersist
  = false`, a hook that never returns delays shutdown instead.
- A failed persist for one lease-lost message is logged and counted as a persist failure instead of aborting the whole
  inbox or outbox pass and discarding the other envelopes' results.
- Every processor and cleanup loop delay goes through the injected `TimeProvider`, so tests can drive the startup delay,
  heartbeat interval and cleanup backoff. The first cleanup retry after a transient failure waits 5 seconds instead of a
  full `Interval`.
- PostgreSQL `AddBatchAsync` inserts in chunks of 1,000 rows inside the caller's transaction, so a batch above the
  65,535 parameter limit no longer fails. Inbox and outbox requeue clear `error_type`, `first_failed_at` and
  `dead_lettered_at`, matching the EF Core and in-memory stores.
- Event post-handlers and error handlers receive `null` as the message result instead of the task that tracked handler
  completion.
- Message resolution counts the interfaces of the registered type, so a message implementing several registered
  interfaces can resolve to a different handler than before.
- `TransportOutboxDispatcherOptions.ResolveRoute` wins over `OutboxEnvelope.Topic`, as its documentation says, and an
  empty result fails the dispatch with `LiteBusDispatchException`.
- A failing diagnostics store stops the queue-depth gauge from reporting until the next successful read, instead of
  exporting the last good counts, and the outbox records `litebus.outbox.diagnostics.unavailable` when no store is
  registered.
- The LiteBus site follows the layout of the Fuse site: a centered landing page with code panes, a durable messaging
  diagram, link cards and the opt-in boundary stated on the page, and documentation entry pages that open with a "This
  page is for" box.
- PostgreSQL modules register every store and the work signal through singleton factories that resolve a
  container-owned `PostgreSqlDataSourceOwner<TModule>`. A data source created by `UseConnectionString` is now disposed
  with the service provider under Microsoft dependency injection, which never disposed the prebuilt instance it was
  registered as before; the documentation already claimed it was. `UseDataSource` still leaves ownership with the
  application.
- `IProcessorWorkSignal` extends `IAsyncDisposable`, so the dedicated LISTEN connection of a PostgreSQL work signal is
  disposed by the container that created it. `PostgreSqlWorkSignal` disposal is idempotent and no longer races a
  listener startup; a wait after disposal falls back to the poll interval.
- The outbox in-process dispatch failure message carries contract-registration guidance only when the cause is a
  missing contract or handler, instead of on every failure including concurrency conflicts and cancellations.
- `MediationExceptionFilters.IsRetryableDispatchException` inspects the inner exception chain, so a refusal or missing
  handler wrapped by a dispatcher is dead-lettered on its first attempt instead of spending the retry schedule.
- The documentation site is rebuilt as a plain, single-stylesheet site: `fumadocs-ui` and Tailwind are removed, and the
  layout, sidebar, search box and code blocks are small components in `site/components/`.

- Every module builder recognizes guard, validator, shortcut, and refusal mapper contracts, completion handler
  contracts, and message definitions as registrable constructs, so `RegisterFromAssembly` discovers them.
- The completion stage orders handlers by priority alone. Every other role runs handlers registered for the message
  type before handlers registered for a base type, but completion handlers observe an ending rather than wrapping the
  handler, so there is no onion for a specific handler to sit inside. The split put the framework's broadly registered
  audit writer beyond the reach of an application's priority, and that order decides whether a record lands inside the
  transaction, which is not something registration breadth should decide. `IMessageDependencies.CompletionHandlers` is
  now the single ordered collection and `IndirectCompletionHandlers` is gone; `IMessageDescriptor` still separates the
  two sets at the registry level.
- `MessageModuleBuilder.UseAuditTrail<TAuditTrail>` takes the lifetime as a parameter, still `Scoped` by default, and
  the instance overload is renamed `UseAuditTrailInstance` so the name carries the lifetime a pre-created instance
  necessarily has. "Scoped when registered by type, Singleton when registered as an instance" was a quiet footgun for
  any trail wrapping a scoped database session, and nothing at the call site said which one was being chosen.
- The `litebus.audit.trail` probe resolves the trail through a dispatch scope rather than from the provider it was
  handed. Resolving a scoped service from a root provider is an error in a container validating scopes, so the probe
  used to fail on exactly the default configuration it exists to approve.
- The registry no longer closes an untyped open generic shortcut for a message that declares a result. A closed
  registration of that pair is still a configuration error, because the author named the message; an open generic says
  "every message I fit" and a result-producing message is not one of them, so it is skipped the way a constraint
  mismatch is.
- `AsyncBroadcastMediationStrategy` observes cancellations so it can report them to the completion stage, honors a guard
  or shortcut decision by publishing to no handlers, and reports no result to completion handlers rather than the task
  that tracked its handlers. Cancellation still propagates as before.
- A decision on a stream query no longer runs post-handlers. Stopping the pipeline means the work did not happen, so
  the reactions to it do not fire; the caller still receives whatever stream the shortcut or the refusal mapper
  supplied.
- Every dispatchable handler contract is declared in one place. `PipelineContracts` holds one row per contract naming
  its family, its invoker, and, for a pre-stage contract, its stage and aggregation policy. Dispatch, all four
  descriptor builders, and the stage runner read from it, so adding the validator stage no longer takes edits in nine
  files, and post-handlers, completion handlers, and refusal mappers are declared the same way rather than hand-wired
  beside the table. The run order is read from the `PreStage` ordinals rather than from a hand-written call sequence,
  which makes the order the enum documents the order that executes.
- The stream mediation strategy routes every fault through one place instead of six, and enumerates the handler's
  stream and a post-handler's replacement through one loop instead of two. It is a third shorter. One timing changes
  with it: the handler's enumerator is released when its enumeration ends rather than when the whole mediation does, so
  it is now disposed before post-handlers run rather than after. A post-handler receives the `IAsyncEnumerable` and
  would enumerate it afresh, so nothing observes this beyond the resource being held for less time.
- The inbox and outbox share one processor hook runner. They ran identical copies, and each built a fresh envelope
  adapter in all five hook phases, so a single dispatch allocated five of them per axis. The adapter is now built once
  per dispatch.
- A pre stage that holds no handler is skipped without enumerating the shared descriptor collection.
  `IMessageDependencies.HasPreStageHandlers` answers from a mask computed once when dependencies are resolved, so a
  message with no guard, validator, or shortcut costs nothing for those stages. The default implementation on the
  interface enumerates and is correct for custom implementations, so nothing outside LiteBus has to change.
- Registering a type that carries a pipeline marker but names no message type is reported with
  `LiteBusConfigurationException` instead of being accepted. Every marker is memberless, so such a type produced no
  descriptor, fell through to message-type registration, and silently never ran.
- Pre-handlers, post-handlers, and completion handlers are invoked through the closed contract recorded in their
  descriptor at registration, using a delegate built while the descriptor is built. The previous dispatch searched a
  handler's interfaces for a method by name on every invocation and called it reflectively, which is how a class
  implementing pipeline contracts for several message types could have the wrong method selected. Choosing the contract
  from registration metadata makes that class of bug structurally impossible, and building the delegate at registration
  keeps reflection out of the dispatch path.
- Two definitions declaring the same value type for one message, or two declarations covering one message where neither
  is more derived than the other, are reported at registration instead of being resolved by assembly scanning order.
- Dependencies are updated to their current versions, which clears the `SSH.NET` advisory reached transitively through
  Testcontainers and restores a clean `NuGetAudit` run. Four are deliberately held back: Roslyn stays on 4.x so the
  analyzer loads on the compiler the .NET 10 SDK ships, EF Core and Npgsql stay on 9.x because
  `Pomelo.EntityFrameworkCore.MySql` has no EF Core 10 provider, and `SQLitePCLRaw` stays on 2.1.x to match the EF Core
  9 SQLite provider.

- `HandlerPriorities.UnitOfWork` moves from `ReservedCeiling` to `ReservedCeiling + 100`. Two names for one value
  invited an application to register infrastructure just above the ceiling and silently tie with the commit, resolved
  by registration sequence, which is assembly scan order. The ceiling is now a pure boundary marker with nothing on it,
  and the band between the two is where application infrastructure that has to run after every LiteBus handler and
  still before the commit belongs.
- `LiteBusConfigurationException` gains derived types so a composition failure can be caught by category:
  `ModuleCompositionException`, `MessageDeclarationException`, `PipelineContractException`,
  `DurableStorageConfigurationException` and `AuditConfigurationException`. It used to be one type for duplicate
  modules, dependency cycles, missing storage, a missing audit trail, refusal mapper conflicts, metadata conflicts and
  untyped-shortcut misuse, so nothing could be caught selectively. The base is no longer sealed and stays catchable as
  the category; every throw site in the library now uses a derived type.
- The command, query and event module builders accept a pipeline handler written against the messaging-level contract
  when its message type is, or is constrained to be, assignable to that axis. A cross-cutting guard had to be written
  once per axis, and the code being copied was authorization, where two copies means one of them gets the next fix. A
  handler constrained to neither axis is still refused, because nothing says which axis it is for and accepting it
  would silently close it over every message in whichever axis happened to register it. Main handler contracts are
  excluded: a command handler and a query handler mean different things.
- `IExecutionContext` is registered as a scoped dependency, so a handler declares it as a constructor parameter and the
  dependency appears in the type signature. Every documented way of reading mediation state went through the
  `AsyncLocal` static, which hides a dependency and forces an ambient scope in a unit test.
  `AmbientExecutionContext` remains the way to reach the context from code that runs outside dependency injection.
- `IAuditOutcomeMapper.MapFailureCode` returns the refusal's own `Code` for a `Denied` or `Invalid` outcome before
  falling back to the exception type name, and no longer reports `LiteBusMessageInvalidException` as a failure code.
  A guard that supplied a code chose it deliberately and it survives either shape of refusal, where an
  exception-derived code is present when a refusal raises and absent when a mapper returns a value instead. A
  shortcut's code is deliberately excluded, because an answered mediation reports `Succeeded` and its code would
  otherwise land in the field a review reads as the reason something did not work.
- `[AuditExempt]` records the same `DeclarationExemption` that `[DeclarationExempt(typeof(AuditDeclaration), ...)]`
  records, through the new `IMessageDeclarationExemptionSource` contract, so every exemption a message carries reads
  from one place whichever spelling wrote it. There is one mechanism with two spellings rather than two mechanisms; the
  paragraph of documentation explaining why auditing was special-cased is gone. `[AuditExempt]` also validates its
  rationale at construction rather than at declaration time.

### Fixed

- The Azure Service Bus publisher no longer throws `ArgumentNullException` for a `TransportPublishRequest` without a
  `MessageId`; the identifier is optional on the request and is now copied only when supplied.
- `MessageRegistry.Handlers` returns a snapshot taken under the registry lock instead of a live view a concurrent
  registration could tear.
- Assembly scans skip types that fail to load instead of aborting the registration, in all six scans.
- A second composition no longer inherits the first one's mediation telemetry options.
- Streams honour the token passed to `WithCancellation` or `GetAsyncEnumerator`, not only the one passed to
  `StreamAsync`.
- `MessageMediator.Mediate` and the public pipeline extension methods throw `ArgumentNullException` naming the argument
  instead of `NullReferenceException`.
- `AddRecurring` registers the command it sends, so a composition that registers the command only through `AddRecurring`
  no longer fails on every pass. A command implementing two result contracts fails at composition, and a missing command
  mediator fails at startup.
- One hook's `AbandonDispatchScope` failure no longer skips the other hooks or leaks the saga correlation gate, and an
  empty message id no longer throws from the abandon path.
- A saga save without an applied message id keeps the existing duplicate-dispatch marker instead of erasing it.
- The PostgreSQL `LISTEN` channel name is quoted.
- A diagnostic probe that observes its token is cancelled when the run budget expires, and a probe whose `Name` differs
  from its registration reports the mismatch instead of a generic failure.
- `GetHostedServiceForBackgroundService<T>()` works before the host has started.
- `BackgroundServiceHostWrapper.StopAsync` completes when the loop ended through the cancellation it requested, instead
  of reporting a failed stop, and `MicrosoftMessageDispatchScope.Dispose` no longer blocks on asynchronous disposal.
- `LiteBusDependencyResolutionException` and `DiagnosticCheckNameMismatchException` throw `ArgumentNullException` for a
  null argument instead of `NullReferenceException`.
- A failed AMQP consumer start releases its channel and can be retried; previously the consumer stayed unusable and
  `WaitUntilStoppedAsync` never completed.
- The in-memory consumer's `StopAsync` and `WaitUntilStoppedAsync` complete after the destination channel is closed
  instead of throwing `ChannelClosedException`, which ended the ingress loop.
- A cancelled ingress acceptance is left unacknowledged for the broker to redeliver instead of being requeued with the
  cancelled token.
- The outbox and inbox retention purge routes report a missing retention policy through the management safety response
  instead of an HTTP 500.
- The site footer's Changelog link points at `Changelog.md`; it returned 404.
- A dispatch cancelled by host shutdown, a drain or a lease loss was recorded as a failure: `EventOutboxDispatcher`
  wrapped the `OperationCanceledException` in `LiteBusDispatchException`, so the envelope handler charged it to the
  retry budget, logged it at Error, pushed the message's visibility out by the backoff, and eventually dead-lettered
  messages that had never failed. The dispatcher now rethrows the caller's cancellation and both envelope handlers treat
  it as no outcome.
- Disposing a transport consumer twice threw `ObjectDisposedException` (Kafka, in-memory, SQS, Azure Service Bus, AMQP
  consumers and `AmqpConnectionManager`). `AzureServiceBusPublisher.Dispose` blocked without a bound and let close
  errors escape; it now waits at most five seconds.
- `BackgroundServiceHostWrapper` leaked its stopping token source and abandoned a loop that ignored shutdown without a
  trace; it disposes the source and logs EventId 6001 naming the service.
- The inbox and outbox cleanup loops threw `OperationCanceledException` out of `ExecuteAsync` when the host stopped
  during their error backoff.
- `TransportInboxIngressConsumer` could recreate its admission semaphore after disposal and release a disposed one.

- Publishing to and consuming from Amazon SQS no longer raises `NullReferenceException`. AWSSDK 4 stopped initializing
  the `MessageAttributes` and `Attributes` collections, which the mapper wrote to and read from directly, so every
  publish failed on the first attribute write. The mapper now supplies its own attribute dictionary and treats an absent
  one on a received message as empty.
- Handler discovery in the analyzers recognizes the two-parameter post-handler contracts and the stream query
  post-handler contract. A handler implementing only those was invisible to LB1011 and LB1012, so an unused
  `[HandlerTag]` on one was not reported.
- An event denial's code reaches the completion stage. `AsyncBroadcastMediationStrategy` captured the decision's reason
  and not its code, so a guard that refused an event with a code left `MessageCompletionContext.Code` empty and the
  audit record uncoded, while the command and query strategies carried it.
- A configuration error thrown from a message definition reads as one. `Describe` is invoked reflectively, so anything
  it threw arrived wrapped in `TargetInvocationException`, which meant a duplicate declaration surfaced as a
  reflection failure the author had to unwrap. The inner exception is now rethrown with its stack intact.
- The event module registers its audit completion handler before the dependency registration pass rather than after,
  so enabling auditing on the event axis no longer fails to resolve the handler on the first audited publish.

### Breaking

- A second `AddLiteBus` on one service collection or container builder throws `ModuleCompositionException`; each
  container hosts one LiteBus composition.
- A second transport module in one composition throws `ModuleCompositionException` naming both modules. Both modules
  register the single `ITransportPublisher` binding, so two transports in one composition could not work before either.
- Registering a service type both as a single binding and as collection items throws `ModuleCompositionException`; the
  single-binding and collection views used to disagree silently.
- `RegisterStartupTask` throws `ArgumentException` for a type that implements `IBackgroundService`, mirroring the
  existing rejection in `RegisterBackgroundService`.
- `ProcessorOptions.HookFailurePolicy` is `ProcessorHookFailurePolicy?`. `null`, the default, means the dispatcher's
  recommendation, otherwise `DeadLetter`. A `UseProcessorOptions` call that sets only a batch size no longer replaces
  the transport dispatchers' `CompleteDespiteHookFailure` with `DeadLetter`; set the policy explicitly to keep that.
- `IProcessorEnvelope`, `IProcessorEnvelopeHook` and `ProcessorHookRunner` moved from
  `LiteBus.DurableMessaging.Abstractions.Processing` to `LiteBus.Messaging.Abstractions.Processing`.
- Idempotency misconfiguration (a blank projected key, a missing context-supplied key) throws
  `IdempotencyConfigurationException` and propagates out of `SendAsync` instead of reaching registered error handlers as
  `AuditConfigurationException`.
- `IOutboxManager.RunRetentionPurgeAsync` throws `OutboxManagementException` when `OutboxCleanupHostOptions.Retention`
  is not a positive period, instead of returning 0.
- A saga purge with no predicate and no `Take` throws `ArgumentException`.
- The AMQP ingress broker-scoped idempotency key is scoped to the queue (`ingress:{queue}:{id}`) instead of the
  exchange, so two queues bound to one exchange no longer share a key. Ingress reads identity and idempotency from the
  `litebus-message-id` header only when `TrustApplicationHeaders` is set. A redelivery that arrives while an AMQP
  ingress host is being redeployed can be accepted twice.
- Kafka and SQS route and content-type metadata use the shared names `litebus-route` and `content-type` instead of
  `Route` and `ContentType`. A consumer reading the old names has to read both during a rollout.
- `TransportPublishRequest.Mandatory` defaults to `true`, matching `AmqpPublishRequest` and the outbox dispatcher; a
  direct `ITransportPublisher` caller relying on `false` has to set it.
- The Kafka transport no longer registers the bare `IProducer<string, byte[]>`; resolve `ITransportPublisher`.
  `MessageTimeoutMs` no longer also sets the socket timeout and disables send retries.
- `LiteBus.Messaging.Extensions.ServiceProviderExtensions.GetRequiredService` is obsolete; use the one in
  `LiteBus.Runtime.Abstractions.Extensions`.
- `IProcessorWorkSignal` now extends `IAsyncDisposable`. A custom work signal must implement `DisposeAsync`, and
  should also implement `IDisposable` when the container may be disposed synchronously.

- `IExecutionContext.Abort` and `LiteBusExecutionAbortedException` are removed. A pre-handler that stopped the pipeline
  now implements a guard contract and returns a `Verdict`, or a shortcut contract and returns a `Shortcut`. The break is
  a compile error rather than a change in behavior, which is deliberate: a flag that left `Abort()` compiling would have
  silently started running the statements after it.
- `ICommandValidator<TCommand>` and `IQueryValidator<TQuery>` return `Task<Validity>` from `ValidateAsync` instead of
  `Task`, and derive from `IMessageValidator<TMessage>` rather than from the pre-handler contract. A validator that
  reported a failure by throwing now returns `Validity.Invalid(...)` instead. The break is a compile error rather than a
  change in behavior, for the same reason the `Abort()` removal is: a validator left compiling would have gone on
  reporting malformed input as a fault. An adapter over an external validation library changes one line, returning the
  failures instead of raising. A codebase with too many validators to convert in one commit derives them from
  `ThrowingCommandValidator` or `ThrowingQueryValidator` to land the build first and convert module by module.
- `IExecutionContext` gained `Data`, and `IMessageDependencies` lost `IndirectCompletionHandlers`. Both are
  infrastructure contracts implemented by LiteBus itself; applications that only implement handlers are unaffected, and
  a custom implementation or test double returns `new HandleContextData()` from `Data` and drops the removed property.
- `MessageModuleBuilder.UseAuditTrail(IAuditTrail)` is renamed `UseAuditTrailInstance`, and the generic overload takes
  an optional `InstanceLifetime`. The rename is a compile error on purpose: the old pair of overloads differed in
  lifetime with nothing at the call site saying so.
- `IModuleConfiguration` gained `CompositionValidations` and `RegisterCompositionValidation`. A custom host adapter must
  run the collected validations after its module loop, or a rule spanning several modules never executes.
- The non-generic pre-stage marker is renamed `IMessagePreStageHandler`. It is the discovery marker for the whole pre
  stage, which now holds four roles, so it can no longer share a name with `IMessagePreHandler<TMessage>`, the one role
  in that stage LiteBus does not name. The post and completion stages hold a single role each and keep the shared name.
- `IAsyncMessageErrorHandler<TMessage>` and `IAsyncMessageErrorHandler<TMessage, TMessageResult>` are renamed
  `IMessageErrorHandler<TMessage>` and `IMessageErrorHandler<TMessage, TMessageResult>`. The prefix named nothing there:
  no synchronous error handler exists, and the contract derives straight from the `IMessageErrorHandler` marker. Every
  stage now follows one rule, that a marker shares its name with its role when the stage holds a single role. The main
  handler keeps `IAsyncMessageHandler`, where the prefix does name something: it is the `Task`-returning specialization
  of `IMessageHandler<TMessage, TMessageResult>`, alongside `IStreamMessageHandler` for the `IAsyncEnumerable` one.
- `IMessageDescriptor` and `IMessageDependencies` gained `RefusalMappers` and `IndirectRefusalMappers`. Custom
  implementations, including test doubles, must add them. `IMessageDependencies.HasPreStageHandlers` ships with a
  default implementation, so it needs no change unless a custom implementation wants the faster answer.
- A refusal or a missing handler now dead-letters on its first attempt in the inbox and outbox processors instead of
  consuming the retry schedule. Both fail identically on every attempt, so retrying only delayed the dead-letter entry
  an operator was waiting to see.
- Only a guard or a shortcut can stop the pipeline. Stopping means skipping the work, and once the main handler has run
  there is nothing left to skip. A handler that previously aborted from a later stage calls `SuppressPostHandlers()`.
- The synchronous handler layer is removed, and the asynchronous one takes over its names. In v6.0.2
  `IMessagePreHandler<TMessage>` declared `object PreHandle(TMessage)` and `IMessagePostHandler<TMessage, TMessageResult>`
  declared `object PostHandle(TMessage, TMessageResult?)`, with `IAsyncMessagePreHandler<TMessage>`,
  `IAsyncMessagePostHandler<TMessage>`, and `IAsyncMessagePostHandler<TMessage, TMessageResult>` holding the `Task`
  members beside them. Every handler is asynchronous now, so the `IAsync` names are gone and the members they declared
  live on `IMessagePreHandler<TMessage>` and `IMessagePostHandler<TMessage, TMessageResult>`. A handler that implemented
  an `IAsync` contract changes the interface name only; a handler that implemented a synchronous one becomes
  asynchronous. The axis contracts such as `ICommandPreHandler<TCommand>` and `IQueryPostHandler<TQuery, TResult>` are
  unchanged at the call site and now derive from those directly.
- `IExecutionContext` gained `PostHandlersSuppressed` and `SuppressPostHandlers()`. Custom implementations, including
  test doubles, must add them.
- `IMessageDescriptor`, `IMessageDependencies`, and the handler descriptor interfaces gained members for the completion
  stage, message metadata, the recorded contract, and the prebuilt dispatch. Custom implementations of these interfaces
  must add them. They are infrastructure contracts implemented by LiteBus itself; applications that only implement
  handlers are unaffected.
- `MessageContextExtensions` moves out of the `LiteBus.Messaging.Abstractions` package and namespace into
  `LiteBus.Messaging`, taking the stage runners with it. They open ambient scopes, order the stages, preserve stack
  traces, and decide what a denied caller receives, which is engine work rather than contract. Only a custom mediation
  strategy names the type, and every package implementing one already references `LiteBus.Messaging`, so the fix is a
  using directive.
- `IPreHandlerDescriptor` is `IPreStageHandlerDescriptor`, and the `PreHandlers` and `IndirectPreHandlers` collections
  on `IMessageDescriptor` and `IMessageDependencies` are `PreStageHandlers` and `IndirectPreStageHandlers`. One
  collection holds all four roles, so `ILazyHandlerCollection<IMessagePreStageHandler, IPreHandlerDescriptor>`
  contradicted itself on a single line.

### Documentation

- The metric catalog lists every public instrument, the package table follows the generated inventory and includes the
  four broker adapters that were missing, and `Test-DocumentationSemantics.ps1` checks the catalog, the package table
  and both axes' management routes against the source.
- `MaxInFlightMessages` is documented with the bound each adapter enforces: 1 for AMQP, Kafka, SQS and in-memory, the
  configured value for Azure Service Bus.
- `ProcessorOptions.LeaseOwner` is documented as a prefix; the processor appends a session suffix that fences instances
  sharing a configured owner. The inbox lease requests document that stores with a database clock use it and treat `Now`
  as the fallback.
- The saga pages state that duplicate-dispatch suppression covers the most recently applied message, describe the
  redelivery case it does not cover, and fix an example that called `SetState` and `Complete()` in one dispatch.
- Corrected namespace attributions for `ILiteBusBuilder` and the processor hook contract, a diagnostic probe sample that
  threw at runtime, shipped idempotency listed as roadmap work, absolute links that sent pre-release readers to v6, and
  the analyzer range, now LB1001-LB1022 with LB1002 and LB1006 reserved and never shipped.
- [Mediation Layer Design Rules](site/content/docs/architecture/mediation-design.md) states the twenty rules the
  mediation layer follows: the stage model and the capability rule, contract and arity shapes, the vocabulary grid,
  what a decision type may express, where each class of configuration error is rejected, and checklists for adding a
  pre-stage role or an axis. Known deviations are listed rather than omitted, so they read as decisions rather than as
  precedents. The layer had a system; it had never been written down, which left every name looking arbitrary.
- The documentation site serves one version per release line, declared in `site/versions.json`. The latest stable line
  stays at `/docs`, so existing links and search results keep working across a release, and every other line carries
  its identifier as a path prefix such as `/v7/docs`. A sidebar switcher moves between versions on the same page,
  search is scoped to the version being read, and a pre-release line is excluded from the sitemap and marked
  `noindex` so a search engine does not offer it ahead of the stable page answering the same question. Until now the
  site served whatever the working branch held, which meant readers on the released package were reading
  documentation for APIs they did not have.
- The pipeline vocabulary is one word per concept, listed in
  [Pipeline Vocabulary](site/content/docs/reference/glossary.md) and enforced across type names, XML comments, and
  the documentation. "Refusal" is the category holding a denial and a validation failure, "denial" is what a guard
  does, and "answered" is what a shortcut does.

## v6.0.2

Patch release for .NET 10. Public APIs, persistence schemas, and transport behavior are unchanged.

### Fixed

- Publishing an event through a base-typed or interface-typed reference now invokes handlers registered for the concrete event type. `IEventMediator.PublishAsync(IEvent, ...)` and the `PublishAsync(@event, cancellationToken)` extension erase the event to `IEvent`, and because handler contracts are contravariant, `AsyncBroadcastMediationStrategy<TMessage>` matched only handlers registered for the erased type and skipped the rest without raising an error. Handlers whose contract does not close over the compile-time message type are now dispatched through the non-generic handler entry point, which routes to the closed contract the handler implements. This also restores in-process outbox dispatch, which publishes every `IEvent` through the non-generic overload.

## v6.0.1

Documentation and policy maintenance release for .NET 10. Public APIs, persistence schemas, and transport behavior are unchanged.

### Added

- `AI_POLICY.md` documents the project's history before public generative coding assistants and defines review, disclosure, provenance, and verification requirements for AI-assisted contributions.
- Repository writing guidance for technical documentation, API descriptions, and capability comparisons.

### Changed

- The root README now focuses on command, query, and event mediation, durable messaging, a compact command example, and direct documentation links.
- Non-migration documentation describes the current LiteBus API and behavior without release-transition narratives. Historical API, package, and database transitions remain in the migration guides.
- The roadmap lists planned work without a version-specific roadmap page.
- Documentation edit links target the `main` branch.

### Fixed

- PostgreSQL schema drift diagnostics describe an incompatible current schema contract without assuming that an existing table belongs to a specific LiteBus release.

## v6.0.0

Major release for **.NET 10** (`net10.0` only). Applications upgrading from v5 must adopt nested module builders,
`AcceptAsync` / `EnqueueAsync`, pipelined processors, and the new durable messaging contracts. PostgreSQL schemas
begin at version 1 in this release and do not mutate LiteBus v5 tables automatically. Follow the
[Migration Guide v6](https://litebus.io/docs/migration/v6) for the API, package, hosting, and database upgrade paths.

### Added

- Role-based project and package dependency policy enforced by `ArchitectureDependencyPolicyTests`.
- Root `Add*Transport` composition extensions for AMQP, Kafka, AWS SQS, Azure Service Bus, and in-memory transports.
- Container-specific dispatch-scope lifecycle coverage for Autofac.
- Axis-specific append results and outbox enqueue outcomes so receipts distinguish new rows from idempotent replays.
- File-backed SQLite and MySQL 8.4 provider contract matrices for both Entity Framework Core durable stores.
- Published `LiteBus.Transport.Testing` xUnit conformance tests for third-party transport adapter authors.
- Evaluated package inventory, source-linked compiled snippets, test-symbol discovery, and semantic documentation gates.
- Shared durable-store contract cases for empty batches, mixed terminal outcomes, complete filters, dead-letter replay,
  and strict idempotency conflicts.
- Broker readiness diagnostics for Kafka, AWS SQS, and Azure Service Bus, including live emulator coverage for each
  configured target.
- Concern-specific `LiteBus.Testing.Mediation`, `LiteBus.Testing.Transport`, `LiteBus.Testing.DurableMessaging`, and
  `LiteBus.Testing.Hosting` packages.

- `IInboxEnvelopeFactory` / `IOutboxEnvelopeFactory` shared by auto-commit writers, store-bound transactional writers,
  and EF interceptors.
- Non-generic `ITransactionalInbox` / `ITransactionalOutbox` with `StoreBoundTransactionalInbox` /
  `StoreBoundTransactionalOutbox`.
- PostgreSQL `CreateTransactionalStore`, `EnableAmbientTransactionProvider()`, and `IPostgreSqlTransactionProvider` for
  ambient participation.
- Writer item/metadata model: `InboxAcceptItem`, `InboxAcceptMetadata`, `OutboxEnqueueItem`, `OutboxEnqueueMetadata`,
  and shared durable value objects in `LiteBus.Messaging.Abstractions.DurableMessaging`.
- [Transactional messaging writes](https://litebus.io/docs/reliable-messaging/transactional-writes) scenario guide.
- `LiteBus.Testing` package with `Test*` mediators, inbox/outbox test doubles, and assertion helpers.
- `ICompositeModule` and nested `InboxModuleBuilder` / `OutboxModuleBuilder` with `UsePostgreSqlStorage`,
  `UseEntityFrameworkCoreStorage`, `UseInMemoryStorage`, `UseInProcessDispatch`, `UseAmqpDispatch`,
  and `UseAmqpIngress`.
- Contract registry split: `IContractWriter` / `IContractReader` on `IMessageContractRegistry`; durable runtime depends
  on read surface only.
- Message registry split: `IMessageWriter` / `IMessageReader` with O(1) `Find`; per-`IModuleConfiguration` registry
  instance (no `Clear()` or `MessageRegistryAccessor`).
- Manifest hosting: `IStartupTask`, `IBackgroundService`, `IDiagnosticCheck` via `IModuleConfiguration`; generic host
  bridges in `LiteBus.Runtime.Extensions.*.Hosting`.
- PostgreSQL storage with v6 version 1 create scripts, indexes, an optional LISTEN/NOTIFY trigger, and
  `GetCreateScript`, `EnsureAsync`, and `ValidateAsync`. No automatic conversion exists from v5.
- EF Core and InMemory inbox/outbox storage; `LiteBus.Storage.Testing` contract harnesses.
- Transport platform: `LiteBus.Transport.Amqp`, Kafka, `LiteBus.Transport.AwsSqs`, Azure Service Bus, InMemory; inbox/outbox dispatch and
  AMQP ingress packages.
- `PipelinedInboxProcessor` / `PipelinedOutboxProcessor` with batch terminal updates, OpenTelemetry meters, retention
  cleanup, dead-letter replay APIs.
- Transactional outbox: `LiteBusOutboxSaveChangesInterceptor`, `ITransactionalOutbox<TContext>`, aligned PostgreSQL
  connection and EF `UseExistingDbContext` participation APIs.
- `LiteBus.Analyzers` rules LB1001, LB1003, LB1004, LB1005, LB1007, LB1008, LB1009, LB1010, LB1011, LB1012, LB1013,
  LB1014 (processor without dispatcher), LB1015-LB1016 (transactional EF/interceptor and DbContext), LB1017 (explicit
  contract registration for attributed types). See [Analyzers](https://litebus.io/docs/reference/analyzers).
- Saga inbox integration (`inbox.EnableSaga()`), payload encryption hooks, tenant lease filters, management and health
  extensions.
- Failure-mode coverage for real worker process termination, Generic Host drain during active dispatch, broker-backed
  shutdown persistence policy, and per-message scoped `DbContext` isolation.
- Repository-owned docs corpus under `site/content/docs/` with [Documentation Index](https://litebus.io/docs), [Migration Guide v6](https://litebus.io/docs/migration/v6),
  [v6 feature index](https://litebus.io/docs/reference/feature-index-v6), and [Capability catalog](https://litebus.io/docs/reference/capability-catalog).

### Changed

- Error handlers now receive `MessageErrorContext<TMessage, TResult>` plus the caller's explicit cancellation token.
  The typed context shares handled outcome and fallback result state with the mediation pipeline.
- `ILiteBusBuilder` moved to `LiteBus.Runtime.Abstractions` and now exposes only `Modules`; feature packages provide
  `AddMessaging`, `AddCommands`, `AddQueries`, `AddEvents`, `AddInbox`, and `AddOutbox`.
- `LiteBus.Orchestration.Abstractions` became `LiteBus.DurableMessaging.Abstractions`, which owns shared durable
  metadata, retry, lease, processor, and hook contracts.
- Inbox and outbox implementation services and module builders moved from abstractions into their core packages.
- Microsoft DI and Autofac adapters now own dispatch-scope creation. Missing scope composition fails, while root
  provider dispatch requires explicit `RootMessageDispatchScopeFactory` registration.
- EF Core inbox and outbox stores use adapter-owned `IDbContextFactory<TContext>` operation contexts.
- Saga storage is selected exactly once inside `EnableSaga(...)`; in-memory storage is no longer an implicit fallback.
- Module dependency validation uses composite ownership and `IRequires<TModule>` without registration markers or
  dependency-registry scans during `Build()`.
- Outbox processor option precedence is independent of configuration call order.
- PostgreSQL inbox, outbox, and saga schemas start at version 1. Validation checks required column types as
  well as columns, indexes, and metadata.
- Inbox and outbox store append methods return ordered append results containing the source-of-truth envelope and
  insertion outcome.
- SQLite EF models store durable timestamps as UTC ticks, and MySQL leasing uses `READ COMMITTED` with a named
  chronological index.
- Test coverage uses one canonical collector configuration and an exact source-line union across every CI batch.
  Pull request and release jobs enforce 90 percent line coverage and treat Codecov upload failures as failures.
- Transport publishers resolve circuit breakers by destination. Ingress recovery no longer shares publisher
  failure state, and half-open recovery admits one probe after a monotonic break duration. Opaque operation permits
  prevent late completions from resetting a newer circuit generation.
- Transport consumers now separate provider-neutral `MaxInFlightMessages` from RabbitMQ and Azure prefetch, SQS
  `ReceiveBatchSize`, and Azure `MaxConcurrentCalls`. Every ingress adapter carries the same nested `Safety` record.
  In-memory destinations now apply configurable, lossless backpressure to queued and in-flight deliveries.
- ASP.NET Core health checks and the management health route expose shared per-probe timeout and parallelism limits.
  The `AddLiteBus()` health registration carries both `litebus` and `ready` tags.
- `LiteBus.Testing` is now a framework-neutral base package. Mediator, transport, durable, and host helpers no longer
  impose unrelated dependency graphs, Newtonsoft.Json, or an assertion library on consumers.

- Package layout: `LiteBus.Storage.PostgreSql`, `LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*`,
  `LiteBus.Transport.Amqp`, `*.Dispatch.InProcess`.
- Neutral inbox/outbox naming: `litebus_inbox_messages.message_id`, `InboxEnvelope`, `OutboxEnvelope`, message-neutral
  processor and store XML.
- `IEventMediator.PublishAsync` is the sole in-process event API.
- `MessageContractAttribute` lives in `LiteBus.Messaging.Abstractions`; explicit `Contracts.Register` recommended for
  durable types.
- Module configuration throws when two modules register the same service type with different bindings.
- PostgreSQL store options default `EnsureSchemaCreationOnStartup = true`; optional validate-only startup for
  migration-owned DDL.
- Analyzer LB1004 targets `IInbox.AcceptAsync`, `AcceptBatchAsync`, `ITransactionalInbox`, and `InboxAcceptItem` rather
  than scheduler APIs.
- Saga: per-dispatch `AsyncLocal` scope, `SagaDefinitionId` and tenant-scoped primary keys, versioned `SagaCompleteItem`,
  dirty-conflict propagation and completion-only retry in `SagaProcessorHook`, `ISagaStore.QueryAsync` / `PurgeAsync`,
  removed `ISaga<TState>`.
- **v5 to v6 API migration:** see [Migration Guide v6](https://litebus.io/docs/migration/v6) for the
  legacy-to-v6 inventory.

### Removed

- The duplicate `AddLiteBus(Action<IModuleRegistry>)` overloads from the Microsoft DI and Autofac adapters. Use the
  single `Action<ILiteBusBuilder>` callback and access `builder.Modules` for custom module registration.
- Preview-only PostgreSQL v6 migration scripts and schema versions. The released inbox, outbox, and saga shapes each
  use one complete version 1 create script.
- The redundant `PostgreSql*Schema.CreateIfNotExistsAsync` aliases. Use `EnsureAsync` for application-managed startup
  checks or `ValidateAsync` when deployment tooling owns DDL.
- The AMQP-specific header alias, circuit-breaker wrapper, and exception. AMQP now uses the shared transport headers
  and destination-scoped circuit-breaker contracts directly.
- The legacy AWS SQS correlation-header fallback. All transport adapters now read and write the canonical
  `TransportHeaders.CorrelationId` key.
- Unused GitVersion configuration. CI and release workflows take their package version from the build or release tag.
- The release-workflow call to a removed documentation script. Release validation now uses the repository's active
  documentation and site checks.

### Fixed

- Typed error handlers can suppress a recoverable exception and return a fallback result without reimplementing an
  untyped synchronous interface. The runtime no longer discards their explicit cancellation token.
- Event parallel fault-mode documentation now matches runtime behavior: already-started sibling tasks settle before
  either one failure or an aggregate is surfaced, and sibling cancellation is never implied.
- The shared Generic Host orchestrator now runs as a supervised `BackgroundService`. An unexpected LiteBus background
  loop fault requests application shutdown immediately instead of leaving the host alive without that workload.
- Closed generic handler registrations retain independent descriptors instead of colliding on one open generic
  definition.
- Inbox and outbox leases use a monotonic generation fence. Renewal and terminal persistence reject stale generations,
  including when the same configured owner reacquires an expired row.
- Direct PostgreSQL and relational EF Core leasing use the database clock for eligibility, expiry, and renewal so an
  application clock offset cannot claim future-visible work or extend a lease incorrectly.
- Inbox and outbox receipts now report exact message-ID and tenant-scoped idempotency replays as `AlreadyAccepted` or
  `AlreadyEnqueued` instead of inferring the outcome from envelope equality.
- MySQL EF leasing now binds nullable tenant filters with a typed provider parameter, reloads the actual identifier
  column, and claims disjoint ordered batches without range-lock starvation or update deadlocks.
- SQLite EF leasing and operator queries now translate timestamp comparisons and ordering instead of failing on
  `DateTimeOffset` expressions.
- Analyzer LB1004 now finds result-bearing commands in inbox batches expressed through local variables, arrays,
  target-typed lists, parenthesized or cast expressions, and collection spreads.
- Open transport circuits no longer extend their deadline when retry loops report another rejection. A failed
  destination cannot block healthy publisher destinations or ingress consumption.
- Azure Service Bus no longer treats prefetch as callback concurrency, SQS no longer silently clamps an overloaded
  prefetch field, and Kafka and in-memory ingress no longer advertise prefetch settings they ignore. Invalid safety,
  SQS receive, and Azure concurrency bounds now fail during module composition.
- In-memory transport publication no longer grows an unbounded channel. Publishers wait asynchronously at the
  configured per-destination capacity, cancellation removes waiting publishers, and requeue retains its reservation.
- Kafka readiness no longer runs a synchronous metadata call inside the diagnostic runner. Provider probes preserve
  caller cancellation, redact SDK error text, and isolate broker failures as unhealthy results.
- AMQP publishers now accept RabbitMQ's empty-name default exchange and scope its circuit by routing key. An already
  canceled publish stops before circuit lookup or broker access.
- ASP.NET management failures now return stable problem details with a request trace identifier. Exception messages
  remain in structured host logs and are not returned to management clients.

- InMemory outbox lease expiry handling for null lease timestamps.
- EF inbox/outbox modules register one singleton store for writer, lease, and state roles.
- PostgreSQL advisory lock keys use independent stable hashes.
- EF in-memory/SQLite leasing filters pending rows before `Take`.
- Thread-safe outbox dispatcher recording for deterministic background processor tests.
- Saga dirty-state conflicts no longer reload and persist stale handler snapshots after a concurrent version advance.
- Transport CI result isolation and skipped-test detection for current VSTest TRX output; live Azure tests use a
  separate opt-in category.

### Breaking changes

- `IMessageMediator.MediateAsync<TMessage, TResult>` was removed because task-returning strategies produced a nested
  `Task<Task>` API. Call `Mediate<TMessage, Task>` or `Mediate<TMessage, Task<TResult>>` and await its returned task.
- `IMessageErrorHandler.HandleError` and scalar `HandleErrorAsync` overloads were replaced by typed-context asynchronous
  methods. The obsolete `IMessageErrorHandler<TMessage, TResult>` marker and `LegacyErrorHandlerSupport` were removed.
- `IMessageTransport` was renamed to `ITransportPublisher`.
- `IRegistrableCommandConstruct`, `IRegistrableQueryConstruct`, and `IRegistrableEventConstruct` were removed.
- `OutboxEnvelope.AsPublished` now requires the publication timestamp.
- Broker dispatch and ingress adapters require one matching root transport module; broker connection settings were
  removed from ingress options and dispatch overloads.
- `LeaseRenewalRequest` now carries `LeaseGeneration`, `LeaseDuration`, and `RequestedExpiresAt` so relational stores
  can calculate expiry from their database clock while in-memory stores retain deterministic clock control.
- PostgreSQL v6 starts each component at schema version 1 because no v6 schema was released before 6.0.0. v5
  tables require replacement or an application-owned data migration.
- Transport modules register `ITransportCircuitBreakerRegistry` instead of one process-wide
  `ITransportCircuitBreaker`. Custom publisher constructors now receive the registry, and the broad
  `TransportPublishFailurePolicy` classification API was removed. Circuit adapters call `AcquirePermit()` and pass
  the returned `TransportCircuitBreakerPermit` to `RecordSuccess` or `RecordFailure`.
- `TransportConsumerOptions.MaxConcurrentMessages` was renamed to `MaxConcurrentCalls`; `ReceiveBatchSize` and
  `MaxInFlightMessages` were added. `AwsSqsInboxIngressOptions.PrefetchCount` became `ReceiveBatchSize`; Kafka and
  in-memory ingress removed `PrefetchCount`. Provider-neutral ingress properties now live under each adapter's
  `Safety` record, including AMQP trust and batch settings.
- AWS SQS and Azure Service Bus root transport modules now register connectivity probes. Configure
  `ConnectivityCheckQueueUrl` or `ConnectivityCheckTarget`; otherwise the registered probe reports degraded instead
  of claiming an unopened SDK client is healthy.
- `IInboxStore` and `IOutboxStore` append methods return `InboxAppendResult` and `OutboxAppendResult`. The redundant
  typed `IOutbox.EnqueueBatchAsync<TEvent>` overload is removed; use the non-generic item batch overload.
- EF application migrations must add `IX_LiteBus_Inbox_CreatedAt` and `IX_LiteBus_Outbox_CreatedAt`. Existing SQLite
  tables must convert durable timestamp columns to UTC ticks stored as `INTEGER`.

- **Target framework:** `net10.0` only (.NET 8 and 9 dropped).
- **Writer APIs:** `IInbox.AcceptAsync` and `IOutbox.EnqueueAsync` replace `AddAsync` / scheduler aliases. Removed
  `InboxOptions`, `OutboxOptions`, `IInboxScheduler`, and `IOutboxScheduler`. Deferred visibility uses `MessageVisibility`
  on `*Metadata`. No obsolete shims.
- **Writer construction:** removed `InboxAcceptItems` and `OutboxEnqueueItems` companion types. Build items with static
  factories on `InboxAcceptItem` / `OutboxEnqueueItem` (`From`, `WithTopic`, `WithIdempotency`, and related helpers).
- **In-process dispatch:** `UseInProcessDispatch()` replaces flat `AddInboxInProcessDispatcher` / event publisher dispatch paths.
- **Processors:** pipelined processors only; sequential legacy loops removed.
- **PostgreSQL schema:** version **1** DDL for inbox, outbox, and saga. Drain and replace v5 tables or run a
  reviewed application-owned data migration; no `GetUpgradeScript` path exists from v5 shapes.
- **Store roles:** `IInboxTerminalStateStore`, retention, and diagnostics interfaces replace monolithic state stores.
- **Registry:** process-wide `MessageRegistry` and `Clear()` removed; one registry per module configuration.
- **Removed APIs:** `IEventPublisher`, `IIdempotentCommand`, v5 `ICommandScheduler` / `AddCommandInboxModule` aliases,
  `ISagaHandler<TCommand,TState>` (use `ISagaContext` in command handlers). See [Saga](https://litebus.io/docs/reliable-messaging/saga).
- **Registration:** flat storage/dispatch/ingress registrars removed; compose inside `AddInboxModule` /
  `AddOutboxModule` only.
- **Composition packages:** removed `LiteBus.Extensions.All`. Use the per-module Microsoft DI packages or the
  `LiteBus.Extensions.Microsoft.DependencyInjection` aggregate package.

### Docs

- Imported the documentation into the main repository and removed the GitHub wiki submodule.
- Added [Documentation Index](https://litebus.io/docs) as the canonical manual entry point.
- Added a compile-checked application sample covering command, query, event, inbox, and outbox composition.
- Added repository checks for relative links, plain ASCII typography, trailing whitespace, and writing-rule phrases.
- Added release checks for benchmark discovery, package metadata, symbol packages, and changelog-derived release notes.

## v5.0.0

### Changed

- `ICommandMediator.SendAsync` now always executes commands immediately in process.
- Durable command scheduling moved to `ICommandScheduler.ScheduleAsync`, which stores `ICommand` envelopes and returns
  `CommandReceipt<TCommand>`.
- Durable event publication now uses `IOutboxWriter.AddAsync` or `IIntegrationOutbox.AddAsync`, which store event
  envelopes and return `OutboxReceipt<TEvent>`.
- Durable inbox and outbox payloads now use stable message contracts with names and versions.
- Durable inbox stores now expose `ICommandInboxWriter`, `ICommandInboxLeaseStore`, and `ICommandInboxStateStore`
  instead of one broad store contract.
- Durable outbox stores now expose `IOutboxMessageWriter`, `IOutboxMessageLeaseStore`, and `IOutboxMessageStateStore`
  instead of one broad store contract.
- Stable outbox message ids now come from `OutboxOptions.MessageId`.
- Event handler predicates now apply to both `PublishAsync(IEvent, settings)` and
  `PublishAsync<TEvent>(TEvent, settings)`.
- Message descriptor resolution failures now throw `MessageDescriptorNotFoundException` with lookup details.
- Message registry namespace filtering now skips only `System` and `System.*` namespaces.
- Unsupported open generic handler shapes now throw `UnsupportedOpenGenericHandlerException`.
- Durable contract registration now supports closed generic message types and rejects open generic message types.
- Persisted contract registration and resolution now use `IMessageContractRegistry` only (`Register`, `GetContract`,
  `GetMessageType`).
- Closed generic messages with concrete handlers now resolve the registered handler type without closing it again.
- The repository now uses `LiteBus.slnx` instead of `LiteBus.sln`.
- CI workflows now restore, build, and test `LiteBus.slnx`, and report Docker availability before PostgreSQL
  Testcontainers tests.

### Added

- Added `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox`, and `LiteBus.Inbox.Storage.PostgreSql`.
- Added `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox`, and `LiteBus.Outbox.Storage.PostgreSql`.
- Added raw Npgsql inbox and outbox stores with leasing, retry visibility, dead-letter state, and Testcontainers
  coverage.
- Added canonical `.sql` schema files in `LiteBus.Storage.PostgreSql`, `LiteBus.Inbox.Storage.PostgreSql`, and
  `LiteBus.Outbox.Storage.PostgreSql` for copy-paste migration ownership.
- Added `IPostgreSqlSchemaLogger` to `LiteBus.Storage.PostgreSql` (Npgsql-only dependency) for optional schema operation
  logging.
- Added `PostgreSqlInboxSchema` / `PostgreSqlOutboxSchema` APIs: `GetCreateScript`, `GetUpgradeScript`, `EnsureAsync`,
  and `ValidateAsync`.
- Added `LiteBus.Inbox.Storage.PostgreSql.Extensions.Microsoft.Hosting` and
  `LiteBus.Outbox.Storage.PostgreSql.Extensions.Microsoft.Hosting` for opt-in schema bootstrap on generic host startup.
- Added `LiteBus.Inbox.Extensions.Microsoft.Hosting` and `LiteBus.Outbox.Extensions.Microsoft.Hosting` for optional
  generic-host processor loops and health checks.
- Added `LiteBus.Storage.PostgreSql.IntegrationTests` with Testcontainers coverage for inbox/outbox stores, schema
  bootstrap and upgrades, drift validation, module registration, and end-to-end processor flows.
- Added `AGENTS.md`, `src/.editorconfig`, and StyleCop documentation analyzers (`GenerateDocumentationFile`) for all
  `src/` projects.
- Added XML documentation on all library types, members, and private/internal fields under `src/`.

### Removed

- Removed the v4 attribute-based command inbox API and related command inbox abstractions.
- Removed `LiteBus.Commands.Extensions.Microsoft.Hosting` because it was tied to the old inbox host.
- Removed `LiteBus.Inbox.Extensions.Autofac` and `LiteBus.Outbox.Extensions.Autofac` because hosting registration lives
  in the Microsoft hosting extension packages (Autofac apps use the same hosting modules through the runtime adapter).
- Removed `IIdentifiedIntegrationEvent`; event identity now belongs to outbox envelope options.
- Removed inbox/outbox processor host interfaces and `UseProcessorHost`; hosting is configured through
  `AddInboxProcessorHosting` / `AddOutboxProcessorHosting` on the hosting extension packages.
- Removed `IMessageContractRegistrar`; contract registration is part of `IMessageContractRegistry`.

### Changed (hosting)

- Moved inbox and outbox processor hosting out of core modules into separate extension packages with self-contained
  `BackgroundService` loops.
- Core inbox/outbox modules now register processors only; they no longer reference Microsoft hosting or health-check
  packages.

### Docs

- Added v5 reliability roadmap, domain event and unit-of-work guidance, and architecture decision records.
- Updated command inbox docs for explicit scheduling semantics, storage metadata, retry, dead-letter, and idempotency
  guidance.
- Added durable outbox docs for writer, processor, dispatcher, PostgreSQL storage, and transaction boundaries.
- Added [PostgreSQL Schema Management](https://litebus.io/docs/integrations/postgresql-schema-management) covering migration-owned DDL, explicit
  bootstrap, opt-in host bootstrap, multi-instance safety, and future upgrade paths.
- Added architecture, dependency graph, and v5 migration docs.
- Added a cookbook recipe for PostgreSQL inbox and outbox registration with processor hosting.
- Added `AGENTS.md` and Cursor rules for XML documentation standards on `src/**/*.cs`.

### Improved

- Expanded PostgreSQL integration tests and fixed cross-test isolation for parallel CI runs.

### Notes

- Inbox and outbox processors deliver **at-least-once** semantics. Handlers and dispatch targets must be idempotent, or
  you must enforce idempotency with application keys such as `CommandScheduleOptions.IdempotencyKey` and
  `OutboxOptions.MessageId`.
- v5 ships durable storage for **PostgreSQL only** (`LiteBus.Inbox.Storage.PostgreSql`,
  `LiteBus.Outbox.Storage.PostgreSql`). Entity Framework Core and SQL Server store packages shipped in **v6**
  (`LiteBus.Inbox.Storage.EntityFrameworkCore`, `LiteBus.Outbox.Storage.EntityFrameworkCore`); dedicated SQL Server
  Npgsql-style packages remain on the [Roadmap](https://litebus.io/docs/roadmap).

## v4.4.0

### Added

- **Post-Handler Result Override:** Post-handlers can now override the result returned to the caller
  by writing a replacement value to `AmbientExecutionContext.Current.MessageResult`. The mediator
  reads this property after the post-handler chain completes and returns it in place of the main
  handler result when non-null. Last write wins when multiple post-handlers write to this property.
  Applies to commands with results and queries. Void commands and events are unaffected.

### Improved

- **Testing Docs (`WebApplicationFactory` isolation):** Added a dedicated wiki section documenting
  the `MessageRegistryAccessor.Instance.Clear()` workaround required when using `WebApplicationFactory`
  in integration tests. Without this call the static `MessageRegistry` retains stale handler
  registrations across tests in the same process, causing intermittent `InvalidOperationException`
  failures on CI.

### Updated

- **Dependencies:** Bumped `Microsoft.Extensions.*` packages to 10.0.8, `Microsoft.SourceLink.GitHub`
  to 10.0.300, `Microsoft.NET.Test.Sdk` to 18.5.1, and `coverlet.*` to 10.0.1.
- **CI:** Updated `softprops/action-gh-release` from v2 to v3 (Node 24 runtime).

## v4.3.0

### Added

- **Open Generic Handler Support:** LiteBus now supports open generic pre-handlers, post-handlers, and error handlers
  (e.g., `MyPreHandler<T> : ICommandPreHandler<T> where T : ICommand`). When registered, LiteBus automatically closes
  the generic for every concrete message type that satisfies its constraints at startup. This enables cross-cutting
  concerns like logging, validation, metrics, and authorization to be implemented once and applied universally, without
  modifying existing messages or handlers. Registration order does not matter. All standard C# generic constraints
  (interface, class, struct, new()) are fully respected.

## v4.2.0

### Added

- **Event Contextual Data:** Added the `Items` property back to `EventMediationSettings`, allowing contextual data to be
  passed through the event mediation pipeline, similar to commands and queries.

### Improved

- **.NET 10 Support:** Added support for .NET 10 across all relevant projects.
- **Developer Experience:** Made assembly signing conditional on the existence of the `LiteBus.snk` file. This
  simplifies the build process for contributors who fork the repository, as they no longer need to generate a strong
  name key to build the project locally.

## v4.1.0

### Added

- **Type-Safe Stream Query Post-Handler:** Introduced the new `IStreamQueryPostHandler<TQuery, TQueryResult>` interface.
  This provides a strongly-typed post-handler for stream queries, giving access to the original query and the
  `IAsyncEnumerable<TQueryResult>` result stream, aligning its design with regular command and query post-handlers.

### Fixed

- **Stream Query Context Preservation:** Fixed a critical bug in the stream query mediation strategy where the
  `AmbientExecutionContext` was lost during stream enumeration. This prevented stream handlers from accessing the
  context (e.g., `Items` collection) in logic that executed after yielding all results, and also prevented stream
  post-handlers from accessing the context. The context is now correctly preserved throughout the entire streaming
  lifecycle.

## v4.0.0

This is a major release with a fundamental architectural redesign to decouple the library from specific Dependency
Injection (DI) containers, introduce a durable Command Inbox, and provide advanced control over event mediation.

### Features

- **Dependency Injection Abstraction (`LiteBus.Runtime`):** The entire library has been refactored to be DI-agnostic,
  introducing a new runtime layer. This decouples the core logic from any specific DI container and allows for
  integrations via a lightweight adapter pattern.
- **Autofac Support:** Added first-class integration with Autofac via the new `LiteBus.Extensions.Autofac` package and
  its companions.
- **Durable Command Inbox:** Introduced the v4 command inbox feature for deferred command execution. This API was
  replaced in v5 by the explicit `ICommandScheduler` and inbox processor contracts.
- **Advanced Event Mediation:** Overhauled event mediation with explicit priority, concurrency, and filtering controls:
- The new `[HandlerPriority]` attribute replaces `[HandlerOrder]` for defining execution priority.
- Added configurable concurrency for both priority groups (`PriorityGroupsConcurrencyMode`) and handlers within the same
  group (`HandlersWithinSamePriorityConcurrencyMode`).
- Enhanced `HandlerPredicate` that receives a full `IHandlerDescriptor` for advanced filtering logic based on handler
  type, priority, tags, and message type.

### Improvements

- **Simplified Module Registration:** The `AddCommandModule`, `AddEventModule`, and `AddQueryModule` extensions now
  automatically register the core `MessageModule`, reducing boilerplate configuration.
- **Registration-Independent Message Registry:** The internal `MessageRegistry` has been re-engineered for improved performance and
  correctness, ensuring handlers are correctly associated with messages regardless of registration order.
- **API Clarity:** Renamed several properties for better intent, such as `Order` to `Priority` on descriptors and
  `Handlers` to `MainHandlers` on `IMessageDependencies`.
- **Testability:** Added `IMessageRegistry.Clear()` to allow resetting the registry state, which is useful in test
  environments.

### Breaking Changes

- **Project Structure & NuGet Packages:** The project structure and package names have been completely refactored. You
  must update your `.csproj` files to reference the new packages (e.g.,
  `LiteBus.Extensions.Microsoft.DependencyInjection`, `LiteBus.Commands.Extensions.Microsoft.DependencyInjection`).
- **DI Registration API:** The `AddLiteBus` registration process is now part of the new DI-specific extension packages.
  Module registration extensions (`AddCommandModule`, etc.) have moved to their respective core namespaces (e.g.,
  `LiteBus.Commands`).
- **Attribute Renaming:** `[HandlerOrder]` has been replaced by `[HandlerPriority]`. The `Order` property on
  `IHandlerDescriptor` is now `Priority`.
- **Mediation Settings `Items` Key:** The key type for the `Items` dictionary on `CommandMediationSettings`,
  `QueryMediationSettings`, and `ExecutionContext` has been changed from `object` to `string`.
- **`EventMediationSettings` Redesign:** The structure of `EventMediationSettings` has been completely changed to
  support the new priority and concurrency features. The `Filters` property is now `Routing`, and a new `Execution`
  property has been added.
- **`IMessageDependencies` Renaming:** The `Handlers` and `IndirectHandlers` properties have been renamed to
  `MainHandlers` and `IndirectMainHandlers`, respectively. This affects custom mediation strategies.

> **Note:** Due to the large architectural changes, please refer to the **v4 Migration Guide** in the release
> notes for detailed instructions on upgrading your project.

## v3.1.0

- **Added**: Support for passing contextual metadata through the mediation pipeline. The `CommandMediationSettings`,
  `QueryMediationSettings`, and `EventMediationSettings` now include an `Items` dictionary (
  `IDictionary<object, object?>`) that can be used to pass data to all handlers (pre-handlers, main handlers,
  post-handlers, and error-handlers) via `AmbientExecutionContext.Current.Items`.

## v3.0.0

- **Breaking Change**: All LiteBus assemblies are now strong-named to support usage in enterprise applications and
  projects that require signed dependencies. This is a breaking change that requires a major version update.

## v2.2.3

- **Fixed**: Remove extra DI container registration

## v2.2.2

- **Fixed**: DI container registration now properly filters out interfaces and abstract classes during service
  registration. Previously, `RegisterFromAssembly()` would cause DI container errors when trying to register
  non-instantiable types. LiteBus message registry continues to accept all types to support polymorphic dispatch, but
  only concrete classes are registered with the DI container.

## v2.2.1

- **Fixed**: Support for record structs as message types (commands, queries, events). Previously record structs couldn't
  be registered due to a type filtering condition that only allowed class types.
- **Improved**: Message registration to handle all non-System types, allowing for greater flexibility in message
  definitions.

## v2.2.0

- **Added**: Support for incremental registration allowing for breaking down LiteBus configuration in different parts of
  the application.

## v2.1.0

- **Added**: .NET 9 support while maintaining backward compatibility with .NET 8
- **Updated**: All dependencies to their latest .NET 9 compatible versions
- **Improved**: Multi-targeting build process for both .NET 8 and .NET 9

## v2.0.0

- **Breaking Change**: Removed nullable annotations from mediator interfaces. Nullability should now be expressed in
  message contracts instead. See [Migration Guides](https://litebus.io/docs/migration) for
  details.

## v1.1.0

- Add `IQueryValidator`

## v1.0.0

- Added: Comprehensive wiki documentation
- Added: Source Link support for improved debugging
- Added: Automated release workflow with GitVersion integration
- Added: Handler tags for contextual scenario handling
- Changed: Updated repository structure for the supported .NET project layout
- Improved: Code documentation and examples
- Fixed: Various minor issues from previous versions

## v0.25.1

- Add `ICommandValidator`

## v0.25.0

- Enable `Nullable` for all projects.

## v0.24.4

- Improve XML comments in the codebase.

## v0.24.3

- Don't throw error by default if no handlers were found for plain event message types

## v0.24.2

- Allow aborting the execution of handlers by calling `Abort` on the execution context.

## v0.24.1

- Add `Tags` to `IExecutionContext`.

## v0.24.0

- Upgraded to .NET 8.

## v0.23.1

- Add `QueryMediatorExtensions` for backward compatibility.
- Add `CommandMediatorExtensions` for backward compatibility.
- Add `EventMediatorExtensions` for backward compatibility.

## v0.23.0

- Fix the missing `Exception` parameter in `IAsyncMessageErrorHandler[TMessage, TMessageResult]` and
  `IAsyncMessageErrorHandler[TMessage]` interfaces.

## v0.22.0

- Introduce tag-based handler filtering through `HandlerTag` and `HandlerTags` attributes.
- Add `CommandMediationSettings` to `ICommandMediator` to allow configuring command mediation.
- Add `QueryMediationSettings` to `IQueryMediator` to allow configuring query mediation.

## v0.21.0

- Fixed Query, Event, and Command error handlers returning `object` instead of `Task`.

## v0.20.2

- Refined Handle Descriptors
- Removed Any Usage of Reflection in `MessageDependencies`
- Removed Some Redundant Code From Descriptors

## v0.20.1

- Rename `AddMessaging` method to `AddMessageModule`.

## v0.20.0

- Revert TargetFramework to NET 7

## v0.19.1

- Add `ThrowOnNoHandlers` to `EventMediationSettings` to allow throwing an exception when no handlers are found for an
  event.
- Fixed a bug where the pre and post handlers were being executed even when no main handlers were found.

## v0.19.0

- Upgraded to .NET 8.

## v0.18.4

- Rename `FilterHandler` to `HandlerFilter` on `EventMediationSettings` as it is more concise and directly states that
  it is a filter for handlers.

## v0.18.3

- Add `EventMediationSettings` to IEventMediator to allow configuring event mediation.
- Add `FilterHandler` to `EventMediationSettings` to allow filtering event handlers.

## v0.18.2

- Preserve the stack trace when rethrowing an exception in case there are no error handlers.

## v0.18.1

- Make execution of event handlers synchronous by default.

## v0.18.0

- All post handlers expose message result as the second parameter.
- Fixed a bug where IEventPreHandler was not asynchronous.
- Added more unit tests.

## v0.17.1

- Add `Items` property to `IExecutionContext` to allow passing data between handlers.

## v0.17.0

- Rename `AddCommands` method to `AddCommandModule`.
- Rename `AddEvents` method to `AddEventModule`.
- Rename `AddQueries` method to `AddQueryModule`.

## v0.16.0

- Introduced execution context using AsyncLocal functionality, accessible through AmbientExecutionContext.
- Renamed `RegisterFrom` to `RegisterFromAssembly` in module builders.
- Standardized namespace for all files in the `LiteBus.Messaging.Abstractions` project to
  `LiteBus.Messaging.Abstractions`, irrespective of folder path.
- Removed `HandleContext` as a parameter from post and pre handlers.

## v0.15.1

- Removed `IEvent` constraint from event handlers, allowing objects to be passed as events without implementing the
  `IEvent` interface.

## v0.15.0

- Added overload method to event publisher for passing an object as a message.
- Removed `LiteBus` prefix from module constructor names.

## v0.14.1

- Upgraded dependency packages.

## v0.14.0

- Upgraded to .NET 7.

## v0.13.0

- Replaced `ICommandBase` with `ICommand`.
- Replaced `IQueryBase` with `IQuery`.
- Renamed `ILiteBusModule` to `IModule`.
- Removed methods `RegisterPreHandler`, `RegisterHandler`, and `RegisterPostHandler`, replacing them with `Register`.
- Removed superfluous base interfaces.

## v0.12.0

- Added support to message registry for registering any class type as a message.

## v0.11.3

- Fixed bug: Execute error handlers instead of pre handlers during error phase.

## v0.11.2

- Fixed bug: Considered the count of indirect error handlers when determining if an exception should be rethrown.

## v0.11.1

- Disabled nullable reference types.
- Ensured error handlers cover errors in pre and post handlers.

## v0.11.0

- Introduced non-generic message registration overloads for events, queries, and messaging configuration.
- Removed the sample project.
- Added unit tests for events and queries.
