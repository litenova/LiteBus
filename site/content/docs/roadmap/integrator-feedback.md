# Integrator Feedback: Analyzer and API Gaps

Integrators report the gaps they hit while building on LiteBus durable messaging. This page records six such gaps from one integrator, each with the observed behaviour, what the source currently does, the options, and a recommendation. It is a design note only. It proposes no immediate implementation and changes no current contract.

Four further sections track deferred work from a later integrator review. They cover saga timeouts and compensation, static checking of declared effects, a library after-commit collector, and a packaged fan-out cursor. Each follows the same shape, and each records the recipe that covers the need until the library work lands.

Six further sections record the outcome of a second NightHall review round, kept only where deferred work remains or a deliberate decision is worth keeping. They cover per-handler outbox delivery, the unit-of-work contract, fault refusal classification, the Marten command package, ambient trace opt-ins, and the declined requests. Each follows the same shape, and each names the guide that documents the shipped behaviour.

## LB1018 Misses Definitions Written Through Describe

### Observation

An integrator declares audit positions through `IMessageDefinition<TMessage>.Describe` (the documented shape for declaring several values at once) and reports that LB1018 still flags those messages, so the team disables the rule.

### Current Behaviour

LB1018 is implemented by `MissingAuditDeclarationAnalyzer`, which delegates to `DeclarationAnalysis.CollectUndeclared` with `AuditDeclaration` as the required value type (see `src/LiteBus.Analyzers/MissingAuditDeclarationAnalyzer.cs` and `src/LiteBus.Analyzers/Analysis/DeclarationAnalysis.cs`, documented in [Analyzers](../reference/analyzers.md)). `DeclarationAnalysis.CollectDefinedMessageTypes` already recognises both definition shapes. The keyed `IMessageDefinition<TMessage, TValue>` contract settles by type arguments. `DescribeDeclares` reads the describing `IMessageDefinition<TMessage>` contract's `Describe` body for `Declare`, `Exempt`, `Audited`, and `NotAudited` calls. Bodies that cannot be read (missing syntax reference, collector passed to a helper) are treated as declared. Definitions in referenced assemblies are not scanned. Only the analyzed assembly is enumerated.

Outcome: `Describe` reading is part of the shipped analyzer, so the blanket report predates it. Direct `Audited`, `NotAudited`, `Declare`, and `Exempt` calls in a `Describe` body in the analyzed assembly are recognised, while three boundaries remain. Application extension methods on `IMessageDeclarations` are skipped, definitions in referenced assemblies are not collected, and unreadable bodies fail open to declared. The runtime path has none of these blind spots, because registration executes `Describe` against a real collector. That is why `RequireDeclaration`, `RequireUniqueAuditActions`, and `RequireAuditActionFormat` are the gap-free backstop behind the analyzer.

### Options

- Extend the existing `DeclarationAnalysis` path to cover the integrator's exact `Describe` shape (to be fixed with a repro), shared by LB1018 and LB1020.
- Add a new analyzer rule for `Describe`-based declarations.

### Recommendation

Closed as document-only. Keep both enforcement layers. LB1018 stays the authoring-time first line. The composition trio (`RequireDeclaration`, `RequireUniqueAuditActions`, `RequireAuditActionFormat`) stays the gap-free backstop, as [Message Definitions](../concepts/message-definitions.md#requiring-a-declaration) already teaches. Do not retire LB1018, because dropping a shipped rule to fix a documentation gap is the wrong trade. The remaining analyzer work stays on the roadmap behind a repro. Teach the shared `DeclarationAnalysis` path the integrator's exact `Describe` shape (an application extension method, a referenced-assembly definition, or an unrecognised call shape) rather than adding a new rule. Keep the fail-open bias for unreadable bodies.

## MessageCompletionContext Has No Public Constructor

### Observation

`MessageCompletionContext<TMessage>` exposes only an internal constructor, so completion handlers (`IMessageCompletionHandler<TMessage>`) cannot be unit tested with a directly constructed typed context without reflection.

### Current Behaviour

`MessageCompletionContext<TMessage>` and `MessageCompletionContext<TMessage, TMessageResult>` (see `src/LiteBus.Messaging.Abstractions/Handler/Completion/`) wrap an untyped `MessageCompletionContext` and construct only from it via internal constructors. The untyped context itself is publicly constructible through required init-only properties (`Message`, `Outcome`, `MessageResult`, `Exception`, `Reason`, `Code`, `Duration`). It exposes public `AsTyped<TMessage>()` and `AsTyped<TMessage, TMessageResult>()` views, so a test can already build the untyped context and project it. The pipeline constructs contexts in `MessageContextExtensions.RunAsyncCompletionHandlers`. Existing test-support homes under `src/` are `LiteBus.Testing` (`ManualTimeProvider`, `LiteBusTestBase`), `LiteBus.Testing.Mediation`, `LiteBus.Testing.DurableMessaging`, and `LiteBus.Testing.Hosting` (see [Dependency Graph](../architecture/dependency-graph.md)).

### Options

- Add a public constructor (or public static factory) on the typed contexts.
- Add a factory in a testing package (for example `LiteBus.Testing.Mediation`) that builds contexts for handler tests.
- Document the `new MessageCompletionContext { ... }.AsTyped<TMessage>()` route and change nothing.

### Recommendation

Add a small factory in `LiteBus.Testing.Mediation` for completion-handler tests. Document the `AsTyped` route in the meantime. Keep the constructors internal. The typed views are pipeline-owned projections. A public constructor would invite production code to synthesise mediation outcomes. The factory keeps test construction discoverable without widening the production surface.

## No Business-Time Field on the Outbox Envelope

### Observation

LiteBus stamps `CreatedAt` from its own clock, so an integrator who needs the domain occurred-at time round-trips it through the event payload.

### Current Behaviour

`OutboxEnvelope.CreatedAt` is storage time. `OutboxEnvelopeFactory.CreateCoreAsync` stamps `_clock.GetUtcNow()` (see `src/LiteBus.Outbox/OutboxEnvelopeFactory.cs`). `OutboxEnvelope` carries no business-time property (see `src/LiteBus.Outbox.Abstractions/Envelopes/OutboxEnvelope.cs`). Per-message annotations live on `OutboxEnqueueMetadata` (`Identity`, `Idempotency`, `Visibility`, `Trace`, `Tenant`, `Target`), projected onto envelope columns by the internal envelope metadata mapper (see `src/LiteBus.Outbox.Abstractions/OutboxEnqueueMetadata.cs`). Per [API Design](../architecture/api-design.md), optional command concerns belong on `*Metadata` as discriminated variants, while the event payload remains the consumer-visible contract.

### Options

- Add an occurred-at variant to `OutboxEnqueueMetadata` (for example alongside `MessageVisibility`), persisted to a new envelope column and mapped through dispatch.
- Leave business time in the payload and add nothing.

### Recommendation

Leave business time in the payload unless querying or retention by occurred-at becomes a requirement. The payload already survives dispatch to consumers, while a metadata field would need a new persisted column, envelope mapping, store support in every adapter, and a transport-header mapping to remain visible after dispatch. If store-side sorting or retention by business time is later required, model it as a metadata variant following the `MessageVisibility` pattern, not as a nullable bag property.

## No Discarded Outbox State

### Observation

Integrators discard dead-lettered rows they will never replay by deleting them. Their tests seed states with raw status integers because no first-class discard exists.

### Current Behaviour

`OutboxStatus` defines `Pending`, `Publishing`, `Published`, `Failed`, and `DeadLettered` (see `src/LiteBus.Outbox.Abstractions/Envelopes/OutboxStatus.cs`). The only terminal write paths are publishing, failing, and dead-lettering (`OutboxEnvelope.AsPublished`, `AsFailed`, `AsDeadLettered`), and requeue strictly from `DeadLettered` (`OutboxEnvelope.AsRequeued`, where `PostgreSqlOutboxStore.RequeueAsync` only moves `DeadLettered` to `Pending`). `IOutboxManager` offers `RequeueAsync`, `RequeueDeadLettersAsync`, and `PurgeAsync` (physical delete with an unrestricted-filter guard), but no discard transition (see `src/LiteBus.Outbox.Abstractions/IOutboxManager.cs`). `OutboxMessageFilter` filters by `Statuses` and ships an `OutboxMessageFilter.DeadLettered` preset (see `src/LiteBus.Outbox.Abstractions/Store/OutboxMessageFilter.cs`).

### Options

- Add an `OutboxStatus.Discarded` terminal value and a manager and operations-store operation (for example `DiscardAsync`) that transitions `DeadLettered` (and optionally `Failed`) rows to it, excluded from `LeasePendingAsync` candidacy and retained for audit.
- Keep deletion-via-`PurgeAsync` as the only discard path.

### Recommendation

Add `Discarded` as a terminal status with a dedicated manager operation. Deletion destroys the audit trail that dead-letter review exists to provide, and raw-integer seeding shows tests need a named value. Scope it to a transition only from `DeadLettered`. Discarded rows are never leased, stay visible in `GetStatusCountsAsync` and `QueryAsync`, and are physically removed only by `PurgeAsync`. Every store adapter (PostgreSQL, Entity Framework Core, InMemory) must handle the new value in lease, persist, and query paths, and the persisted integer mapping needs a migration note since statuses are stored as integers.

## Open Generic Handlers and the Double-Run Report

### Observation

An integrator reports that an open generic pipeline handler closes over both a base interface (for example `ICommand`) and each concrete type, running the stage twice for one message.

### Current Behaviour

The report was accurate and depended on registration order. `MessageRegistry.TryCloseOpenGenericHandler` closes an open generic over every registered message that fits its constraint, and a base interface such as `ICommand` is a registered message as soon as any handler names it, so `Guard<T> where T : ICommand` was also closed as `Guard<ICommand>`. That closure was linked indirectly to commands registered after it but not to commands already committed, so the guard ran twice for a command registered last and once for a command registered first.

Outcome: fixed. `MessageDescriptor.AddDescriptor` keeps only the direct closure when two closures of one generic handler definition reach a message through the same contract, in either arrival order (see `src/LiteBus.Messaging/Registry/Descriptors/MessageDescriptor.cs`). A different handler type that names the base interface still runs indirectly, before direct handlers, as designed. A handler class that implements both a base and a concrete contract still runs twice, and `LiteBusCompositionSummary.DuplicateStageCoverages` reports it at startup. Regression tests pin both registration orders and the assembly scan order.

### Options

- Treat as a bug and dedupe (for example skip an indirect entry whose handler type is already present as a direct entry).
- Document the direct and indirect composition rule and improve its visibility.

### Recommendation

Closed: dedupe closures of one generic handler definition, and nothing else. Indirect-then-direct execution stays the base-type extensibility rule for distinct handler types (a cross-cutting handler over `ICommand` plus a message-specific one), so no registered handler class is dropped. Two closures produced from one open generic registration are not two registrations, and running them both made the outcome depend on registration order. The rule is documented on [Open Generic Handlers](../concepts/open-generic-handlers.md). The startup report on `LiteBusCompositionSummary` names the remaining double (one class implementing both contracts), and `IMessageReader.Explain` shows each step with `IsIndirect` and `IsClosedOpenGeneric`. No analyzer follows, because the startup report is the detection surface.

## Health Check and Outbox Telemetry Are Opt-In and Undiscovered

### Observation

The integrator referenced packages with no health check or outbox telemetry, and did not find the opt-in packages that provide them.

### Current Behaviour

Health checks ship in `LiteBus.Extensions.Diagnostics.HealthChecks`. `LiteBusHealthCheck` runs the `IDiagnosticCheck` probes collected on the host manifest (`LiteBusHealthCheckExtensions.AddLiteBus` registers it, in `src/LiteBus.Extensions.Diagnostics.HealthChecks/`). Telemetry ships per concern in `LiteBus.Messaging.Extensions.OpenTelemetry`, `LiteBus.Inbox.Extensions.OpenTelemetry`, `LiteBus.Outbox.Extensions.OpenTelemetry`, and `LiteBus.Transport.Extensions.OpenTelemetry` (see [Dependency Graph](../architecture/dependency-graph.md)). Backlog and state introspection live on `IOutboxManager` (`GetStatusCountsAsync`, `GetRetentionStatusAsync`, `GetSchemaInfoAsync`, `QueryAsync`). None of these are referenced by default, per the opt-in packaging rule. A read-only publisher must not transitively install health-check or telemetry dependencies.

### Options

- Reference the opt-in packages by default from broader bundles.
- Keep them opt-in and document the mapping from operational need to package.

### Recommendation

Keep the packages opt-in and document the mapping, as the integrator suggests. Widening default references would violate the opt-in packaging rule the roadmap elsewhere preserves. The documentation should map each need directly:

- Backlog and dead-letter depth to `IOutboxManager.GetStatusCountsAsync` plus the `LiteBusHealthCheck` probe surface.
- Dispatch latency and failure rates to the matching `*.Extensions.OpenTelemetry` package.
- Retention behaviour to `GetRetentionStatusAsync` and `RunRetentionPurgeAsync`.

A short operations recipe per need beats a meta-package that drags every SDK into every service.

## Saga Timeouts and Compensation

### Observation

An integrator orchestrates multi-step flows from reactions plus mediator fan-out. A step handler reads a set and dispatches N commands, with catch-and-stop control flow shared by copy. They ask for a durable saga primitive with correlated state, one handler per step, outbox-backed step dispatch, and compensation and timeout hooks, so a flow is one record with visible state instead of N reactions sharing an implicit protocol.

### Current Behaviour

A saga module already exists and covers correlated inbox state. `EnableSaga` on the inbox builder enables it, handlers use `SagaCorrelation` plus `ISagaContext` (`GetState`, `SetState`, `Complete`), `ISagaStore` adds optimistic concurrency, and a processor hook loads before dispatch and saves or completes after it (see [Saga](../reliable-messaging/saga.md)). What it does not cover is exactly the remainder of the ask. There is no dedicated step contract (steps are ordinary `ICommandHandler<TCommand>` implementations), no outbox-backed step dispatch, no timeout or scheduling framework, and no compensation language. Those exclusions are documented on the saga page, which is also where the at-least-once durability boundary and the saga state evolution rule live.

### Options

- Document the saga that already exists plus a per-page dispatch recipe, leaving timeouts and compensation as application code.
- Build saga orchestration as an engine: outbox-backed step dispatch through a new feature bridge, durable timeouts via deferred visibility, and an explicit compensation path.

### Recommendation

Defer the engine. The urgent need, one correlated record instead of N reactions, is already met by inbox saga state, so the highest-value change is the guide and the existing shape. Timeouts, compensation, and outbox-backed choreography stay on the roadmap alongside the [Saga Application Ledger](reliability.md#saga-application-ledger), which lists the transaction and compensation questions as unsolved. They touch persisted formats and cross-axis mapping that cannot land safely on the stable boundary.

## Static Checking of Declared Effects

### Observation

An integrator declares per-use-case effects (issues, sends, publishes, calls, each with inline, after-commit, or async timing) in external records enforced by an external checker. LiteBus cannot see these declarations, so composition, dispatch, and analyzers cannot verify that a handler dispatches only its declared edges. They ask for effect metadata on messages or handlers plus an analyzer that checks actual `SendAsync` and `PublishAsync` calls against it.

### Current Behaviour

No effect vocabulary exists in the library, and none is planned as library API. Four verbs and three timings are one application's taxonomy, and blessing them in mediation contracts would strain the role boundary around durable timing.

The generic declaration machinery already carries the application-owned form with no new API. An application record declared per message through the keyed definition shape gets presence enforced by `RequireDeclaration` at composition and LB1020 at compile time, and value shape enforced by a `ValidateComposition` callback over `IMessageCatalog` (see [Declaring Effects](../concepts/message-definitions.md#declaring-effects)). What no shipped API does is verify dispatched calls against the declared edges. `Explain` lists registered handlers rather than the calls a handler makes, and nothing records which messages a handler actually dispatched.

### Options

- Keep effects application-owned with the declaration recipe plus a runtime edge audit (mediator decorators recording dispatched types into `IExecutionContext.Data`, diffed against the declaration by a completion handler), all buildable on shipped seams.
- Add a library analyzer rule comparing `SendAsync` and `PublishAsync` call targets against declared effects.

### Recommendation

Defer the analyzer. The recipe gives declared, presence-enforced, shape-checked effects today, and the runtime decorator can be the gap-free check the integrator wants. Static checking is limited to attribute-declared edges with statically readable type arguments, because the analyzer never executes `Describe` bodies, so a rule would either silently pass `Describe`-declared edge lists or force an attribute-only style the documentation otherwise discourages. That ceiling makes it roadmap material, if it lands at all.

## A Library After-Commit Collector

### Observation

An integrator's command handlers send buyer notifications inline inside the command transaction through an application messenger, while their specifications declare those messages after-commit. A mail-provider stall then fails a money write. The outbox exists, but staging one notification through it costs an integration event, a reaction, and a send per message. They ask for an `IAfterCommit` (or `ctx.Defer`) effect that a handler records during its unit of work and that LiteBus writes to the outbox with the aggregate, then dispatches after commit with retry and deduplication.

### Current Behaviour

No defer API exists. There is no `IAfterCommit`, no `Defer`, and no after-commit variant of `MessageVisibility`. The durable retry and deduplication the ask needs are already expressible on shipped APIs:

1. Collect consequences on the aggregate or in `IExecutionContext.Data` during handling.
2. Stage them through `ITransactionalOutbox` from a completion handler ordered before the `UnitOfWork` commit.
3. Dispatch after commit to the existing sender (see [After-Commit Notifications Through the Outbox](../reliable-messaging/domain-events-and-unit-of-work.md#after-commit-notifications-through-the-outbox)).

A library collector would save staging boilerplate but not the per-message event plus contract plus handler cost, because an outbox row is a serialized message with a contract name and version, and arbitrary callbacks cannot be deferred durably.

### Options

- Keep the documented collector recipe on shipped APIs and change nothing.
- Ship an outbox-backed deferred-message collector: a collector interface in the outbox axis plus a staging completion handler, so recording an effect is one call rather than a hand-built completion handler.

### Recommendation

Defer the collector. Recording an effect during mediation and having LiteBus persist it pushes against the rule that mediation and durable persistence stay separate calls with separate semantic types, and a `Defer` API without a commit-boundary hook is incomplete while host-neutral transaction and unit-of-work hooks are themselves still on the roadmap. Revisit alongside those hooks and declared effects, which are its missing prerequisites.

## A Packaged Fan-Out Cursor

### Observation

An integrator's reactions page every confirmed order of an event and send per order inside one reaction or command, holding the pipeline (and in the command case, a transaction) for the whole fan-out. They ask for a durable cursor primitive. The command processes one page, stores its cursor, and re-enqueues itself until done, with a lease so a second instance does not run the same page.

### Current Behaviour

No durable fan-out or execution-cursor primitive exists. The only cursor types paginate operator listings, not execution. The safe shape already composes from shipped APIs:

1. Make the fan-out unit an inbox command carrying a fan-out id and a cursor.
2. Page N rows per dispatch.
3. Accept the next page as a new inbox message when the page comes back full, so each page is a separately leased message and two instances never execute the same page concurrently (see [Paged Fan-Out](../reliable-messaging/paged-fan-out.md)).

Saga state keyed by the same correlation is available when the cursor must be queryable. The application still owns the packaging (cursor encoding, termination, poison-page handling, and completion signalling).

### Options

- Keep the documented self-re-enqueue recipe on one axis and change nothing.
- Ship a packaged helper: a page-handler contract standardising page size, cursor advance, self-re-enqueue, and terminal conditions, with leasing and retry staying with the axis processor.

### Recommendation

Defer the packaged primitive. Cursor encoding, tenant scoping, poison pages, and completion signals are easy to get subtly wrong, and a cursor service with owned rows would duplicate much of what inbox messages plus saga rows already do. It should not precede settled answers for recurring single-flight leases and the saga ledger work, so it stays on the roadmap behind those two.

## Per-Handler Outbox Delivery

### Observation

In-process outbox dispatch publishes one envelope to every handler of the event, so one failing reaction makes the retry run every reaction again, including the ones that already succeeded.

### Current Behaviour

Outcome: shipped as an opt-in on the in-process dispatch path. Under the opt-in, an enqueue writes one envelope per subscribed main handler of the event, each recording that handler's identity, and dispatch publishes each envelope to its handler alone, so every reaction gets its own retry, dead letter, and replay (see [Outbox](../reliable-messaging/outbox.md)). The cost is one row per handler instead of one row per event, and a handler added after enqueue does not receive already-enqueued events, because its envelope was never written.

### Options

- Keep one envelope per event, so a retry replays every handler.
- Write one envelope per handler, so each reaction retries alone.

### Recommendation

Closed: ship the per-handler shape as an opt-in, not the default. Isolated retry is what most event consumers want, but the extra rows and the added-after-enqueue limitation change store growth and delivery semantics enough that existing compositions must keep their current behaviour until they choose it.

## A Unit-of-Work Contract

### Observation

LiteBus documents committing in a completion handler at `HandlerPriorities.UnitOfWork` but ships none of it, so integrators write the same commit handler once per application.

### Current Behaviour

Outcome: shipped. `IUnitOfWork` in `LiteBus.Messaging.Abstractions` commits work the handler staged, and `CommandModuleBuilder.UseUnitOfWork<TUnitOfWork>()` plus `QueryModuleBuilder.UseUnitOfWork<TUnitOfWork>()` register the scoped implementation with a completion handler at `HandlerPriorities.UnitOfWork` that calls `CommitAsync` only when the mediation completed successfully (see [Domain Events and Unit of Work](../reliable-messaging/domain-events-and-unit-of-work.md)). LiteBus still never opens or owns a transaction. The application's implementation does the commit, and the library only decides when it runs.

### Options

- Ship the contract and the completion handler, keeping transaction ownership with the application.
- Keep the commit recipe application-owned and ship nothing.

### Recommendation

Closed: ship the contract. The timing rule (commit once, only on success, after staging) is the same in every application, while the commit itself stays application code, so the boundary the recipe taught is the boundary the library enforces.

## Fault Refusals From Any Stage

### Observation

Error handlers never see a fault raised in the completion stage, so a duplicate-key or concurrency fault at commit cannot become a typed refusal and every application maps it by hand.

### Current Behaviour

Outcome: shipped. `IFaultRefusalClassifier` plus `MessageModuleBuilder.UseFaultRefusalClassifier<TClassifier>()` turns a recognised fault from any stage, the completion stage and the main handler included, into the classifier's refusal. `TrySendAsync` and `TryQueryAsync` return it as a value, `SendAsync` throws the refusal's exception, and refusal mappers and audit see a refusal rather than a failure (see [Handler Pipeline](../concepts/handler-pipeline.md)). Cancellation is never classified, and nothing changes when no classifier is registered. `LiteBus.Storage.PostgreSql` ships a constraint classifier mapping unique-violation (23505) and exclusion-violation (23P01) errors to refusals by constraint name.

### Options

- Classify recognised faults into refusals at the mediation boundary.
- Keep faults as faults and leave the mapping to application code.

### Recommendation

Closed: ship the classifier. A commit fault reaches the caller as a refusal the caller branches on, and classifying it where the mediation already ends keeps one refusal path instead of two error paths.

## The Marten Command Package

### Observation

NightHall builds on Marten and PostgreSQL and asks for Marten support. It needs a unit of work over the document session, an idempotency store, and an audit trail that commits with the handler's change.

### Current Behaviour

Outcome: shipped as `LiteBus.Commands.Storage.Marten`, a feature bridge holding `MartenUnitOfWork` (registered through `UseMartenUnitOfWork`, which calls `UseUnitOfWork<MartenUnitOfWork>()`), `MartenIdempotencyStore`, and `MartenAuditTrail`, which stages each record in the scoped session so it commits with the handler's change in the same `SaveChangesAsync` (see [Marten Unit of Work, Idempotency, and Audit](../integrations/commands-marten.md)). It is deliberately not a `LiteBus.Marten` kitchen-sink package. Per the granular opt-in packaging rule, consumers reference only what they run, and a Marten command host must not transitively install the outbox Marten store it never uses.

### Options

- Ship one `LiteBus.Marten` package with every Marten adapter inside.
- Ship one package per concern, following the existing storage-adapter split.

### Recommendation

Closed: ship the per-concern package. The kitchen-sink shape would trade one convenient reference for unwanted transitive dependencies in every consumer, which is exactly the debt signal the packaging rule names.

## Ambient Causation as an Opt-In

### Observation

NightHall asks for automatic causation. Durable envelopes should carry the ambient correlation and causation without every accept and enqueue call naming a trace explicitly.

### Current Behaviour

Outcome: shipped as module opt-ins. `InboxModuleBuilder.UseAmbientTrace()` and `OutboxModuleBuilder.UseAmbientTrace()` make the envelope factory use `MessageTrace.FromAmbient()` when an item's `Metadata.Trace` is `MessageTrace.None`. An explicit trace always wins, and outside a mediation the result is `None` as today (see [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md)). It is not the default, because it changes persisted envelopes. Existing consumers and stored rows keep their current shape until the application opts in.

### Options

- Stamp the ambient trace by default on every durable envelope.
- Stamp it only under a module opt-in, keeping explicit-only as the default.
- Keep causation explicit on every item and ship nothing.

### Recommendation

Closed: ship the opt-ins. Automatic causation removes real boilerplate, but a default that rewrites persisted envelopes would change stored data for compositions that never asked, so the application chooses per axis.

## Asks Left Out

### Observation

The same round asked for four further primitives. The asks were uniqueness reservations, cron scheduling for recurring commands, in-memory after-commit callbacks, and LiteBus-owned transactions.

### Current Behaviour

Each ask already has an answer on shipped APIs. Uniqueness is a store guarantee that unique indexes and exclusion constraints arbitrate in the database, and the constraint refusal classifier turns violations into typed refusals. Recurring commands are deliberately a poll loop. The loop has no cron expression, no wall-clock alignment, and no missed-run catch-up (see [Recurring Commands](../integrations/recurring-commands.md)). An outbox row is a serialized message with a contract name and version, so arbitrary callbacks cannot be deferred durably, and after-commit work that matters already flows through the outbox with retry. The shipped `IUnitOfWork` calls the application's commit and nothing more.

### Options

- Ship the four primitives as library API.
- Decline them and keep the existing boundaries.

### Recommendation

Closed as declined. Uniqueness reservations would duplicate what the database already arbitrates. Cron scheduling would turn a poll loop into a scheduler with missed-run semantics the loop deliberately does not own. In-memory after-commit callbacks would silently lose work on crash, contradicting the durability boundary the outbox exists to provide. Owning transactions would make LiteBus the transaction owner every axis is carefully not.
