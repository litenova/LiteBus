# Integrator Feedback: Analyzer and API Gaps

This page records six gaps reported by an integrator working against LiteBus durable messaging, each with the observed behaviour, what the source currently does, the options, and a recommendation. It is a design note only: it proposes no immediate implementation and changes no current contract.

## LB1018 Misses Definitions Written Through Describe

### Observation

An integrator declares audit positions through `IMessageDefinition<TMessage>.Describe` (the documented shape for declaring several values at once) and reports that LB1018 still flags those messages, so the team disables the rule.

### Current Behaviour

LB1018 is implemented by `MissingAuditDeclarationAnalyzer`, which delegates to `DeclarationAnalysis.CollectUndeclared` with `AuditDeclaration` as the required value type (see `src/LiteBus.Analyzers/MissingAuditDeclarationAnalyzer.cs` and `src/LiteBus.Analyzers/Analysis/DeclarationAnalysis.cs`, documented in [Analyzers](../reference/analyzers.md)). `DeclarationAnalysis.CollectDefinedMessageTypes` already recognises both definition shapes: the keyed `IMessageDefinition<TMessage, TValue>` contract (settled by type arguments) and the describing `IMessageDefinition<TMessage>` contract, whose `Describe` body is read by `DescribeDeclares` for `Declare`, `Exempt`, `Audited`, and `NotAudited` calls. Bodies that cannot be read (missing syntax reference, collector passed to a helper) are treated as declared, and definitions in referenced assemblies are not scanned: only the analyzed assembly is enumerated.

### Options

- Extend the existing `DeclarationAnalysis` path to cover the integrator's exact `Describe` shape (to be fixed with a repro), shared by LB1018 and LB1020.
- Add a new analyzer rule for `Describe`-based declarations.

### Recommendation

Teach LB1018 the missing shape rather than adding a new rule. The shared `DeclarationAnalysis` route is the right seam because LB1018 is documented as the preconfigured instance of LB1020; a second rule would split one requirement across two diagnostics. First reproduce with the integrator's definition to find which boundary it hits (referenced-assembly definition, custom collector extension, or an unrecognised call shape), then extend `DescribeDeclares` or the assembly scope accordingly, keeping the fail-open bias for unreadable bodies.

## MessageCompletionContext Has No Public Constructor

### Observation

`MessageCompletionContext<TMessage>` exposes only an internal constructor, so completion handlers (`IMessageCompletionHandler<TMessage>`) cannot be unit tested with a directly constructed typed context without reflection.

### Current Behaviour

`MessageCompletionContext<TMessage>` and `MessageCompletionContext<TMessage, TMessageResult>` (see `src/LiteBus.Messaging.Abstractions/Handler/Completion/`) wrap an untyped `MessageCompletionContext` and construct only from it via internal constructors. The untyped context itself is publicly constructible through required init-only properties (`Message`, `Outcome`, `MessageResult`, `Exception`, `Reason`, `Code`, `Duration`), and exposes public `AsTyped<TMessage>()` and `AsTyped<TMessage, TMessageResult>()` views, so a test can already build the untyped context and project it. The pipeline constructs contexts in `MessageContextExtensions.RunAsyncCompletionHandlers`. Existing test-support homes under `src/` are `LiteBus.Testing` (`ManualTimeProvider`, `LiteBusTestBase`), `LiteBus.Testing.Mediation`, `LiteBus.Testing.DurableMessaging`, and `LiteBus.Testing.Hosting` (see [Dependency Graph](../architecture/dependency-graph.md)).

### Options

- Add a public constructor (or public static factory) on the typed contexts.
- Add a factory in a testing package (for example `LiteBus.Testing.Mediation`) that builds contexts for handler tests.
- Document the `new MessageCompletionContext { ... }.AsTyped<TMessage>()` route and change nothing.

### Recommendation

Add a small factory in `LiteBus.Testing.Mediation` for completion-handler tests and document the `AsTyped` route in the meantime. Keep the constructors internal: the typed views are pipeline-owned projections, and a public constructor would invite production code to synthesise mediation outcomes. The factory keeps test construction discoverable without widening the production surface.

## No Business-Time Field on the Outbox Envelope

### Observation

LiteBus stamps `CreatedAt` from its own clock, so an integrator who needs the domain occurred-at time round-trips it through the event payload.

### Current Behaviour

`OutboxEnvelope.CreatedAt` is storage time: `OutboxEnvelopeFactory.CreateCoreAsync` stamps `_clock.GetUtcNow()` (see `src/LiteBus.Outbox/OutboxEnvelopeFactory.cs`), and `OutboxEnvelope` carries no business-time property (see `src/LiteBus.Outbox.Abstractions/Envelopes/OutboxEnvelope.cs`). Per-message annotations live on `OutboxEnqueueMetadata` (`Identity`, `Idempotency`, `Visibility`, `Trace`, `Tenant`, `Target`; see `src/LiteBus.Outbox.Abstractions/OutboxEnqueueMetadata.cs`), projected onto envelope columns by the internal envelope metadata mapper. Per [API Design](../architecture/api-design.md), optional command concerns belong on `*Metadata` as discriminated variants, while the event payload remains the consumer-visible contract.

### Options

- Add an occurred-at variant to `OutboxEnqueueMetadata` (for example alongside `MessageVisibility`), persisted to a new envelope column and mapped through dispatch.
- Leave business time in the payload and add nothing.

### Recommendation

Leave business time in the payload unless querying or retention by occurred-at becomes a requirement. The payload already survives dispatch to consumers, while a metadata field would need a new persisted column, envelope mapping, store support in every adapter, and a transport-header mapping to remain visible after dispatch. If store-side sorting or retention by business time is later required, model it as a metadata variant following the `MessageVisibility` pattern, not as a nullable bag property.

## No Discarded Outbox State

### Observation

Integrators discard dead-lettered rows they will never replay by deleting them, and their tests seed states with raw status integers because no first-class discard exists.

### Current Behaviour

`OutboxStatus` defines `Pending`, `Publishing`, `Published`, `Failed`, and `DeadLettered` (see `src/LiteBus.Outbox.Abstractions/Envelopes/OutboxStatus.cs`). The only terminal write paths are publish, fail, dead-letter (`OutboxEnvelope.AsPublished`, `AsFailed`, `AsDeadLettered`), and requeue strictly from `DeadLettered` (`OutboxEnvelope.AsRequeued`; `PostgreSqlOutboxStore.RequeueAsync` only moves `DeadLettered` to `Pending`). `IOutboxManager` offers `RequeueAsync`, `RequeueDeadLettersAsync`, and `PurgeAsync` (physical delete with an unrestricted-filter guard), but no discard transition (see `src/LiteBus.Outbox.Abstractions/IOutboxManager.cs`). `OutboxMessageFilter` filters by `Statuses` and ships an `OutboxMessageFilter.DeadLettered` preset (see `src/LiteBus.Outbox.Abstractions/Store/OutboxMessageFilter.cs`).

### Options

- Add an `OutboxStatus.Discarded` terminal value plus a manager and operations-store operation (for example `DiscardAsync`) that transitions `DeadLettered` (and optionally `Failed`) rows to it, excluded from `LeasePendingAsync` candidacy and retained for audit.
- Keep deletion-via-`PurgeAsync` as the only discard path.

### Recommendation

Add `Discarded` as a terminal status with a dedicated manager operation. Deletion destroys the audit trail that dead-letter review exists to provide, and raw-integer seeding shows tests need a named value. Scope it: transition only from `DeadLettered`, never leased, visible in `GetStatusCountsAsync` and `QueryAsync`, physically removed only by `PurgeAsync`. Every store adapter (PostgreSQL, Entity Framework Core, InMemory) must handle the new value in lease, persist, and query paths, and the persisted integer mapping needs a migration note since statuses are stored as integers.

## Open Generic Handlers and the Double-Run Report

### Observation

An integrator reports that an open generic pipeline handler closes over both a base interface (for example `ICommand`) and each concrete type, running the stage twice for one message.

### Current Behaviour

A single open generic registration closes exactly once per fitting concrete message; there is no path that yields two descriptors for one message from one registration. `MessageRegistry.TryCloseOpenGenericHandler` builds one type-argument set per message (`BuildOpenGenericTypeArguments` returns the message type itself for arity-1 handlers, or message plus declared result type for arity-2), closes the handler with `MakeGenericType`, and links the resulting descriptors to that message (see `src/LiteBus.Messaging/Registry/MessageRegistry.cs`). `MessageDescriptor.AddDescriptor` then routes exact-type matches to the direct collections and base-type or interface matches to the indirect collections (see `src/LiteBus.Messaging/Registry/Descriptors/MessageDescriptor.cs`). A second run for one mediation requires two registrations: for example an open generic closed per concrete message (direct) plus a handler closed over the base interface such as `ICommand` (indirect). The pipeline runs both collections: `MessageContextExtensions.RunStage` runs indirect pre-stage handlers before direct ones, validation, error, and post-handler paths iterate both sets, completion handlers concatenate both, and only main-handler resolution prefers direct over indirect (`SingleMainHandlerResolver`).

### Options

- Treat as a bug and dedupe (for example skip an indirect entry whose handler type is already present as a direct entry).
- Document the direct and indirect composition rule and improve its visibility.

### Recommendation

Document, do not dedupe. Indirect-then-direct execution is the designed base-type extensibility rule (a cross-cutting handler over `ICommand` plus a message-specific one), and identity-based dedupe would silently drop a handler the application registered deliberately. Reduce surprise instead: document the rule on [The Handler Pipeline](../concepts/handler-pipeline.md), and evaluate a composition-time hint (via `LiteBusCompositionSummary` open-generic closure counts or a new analyzer) when the same handler type appears in both the direct and indirect sets for one message.

## Health Check and Outbox Telemetry Are Opt-In and Undiscovered

### Observation

The integrator referenced packages with no health check or outbox telemetry, and did not find the opt-in packages that provide them.

### Current Behaviour

Health checks ship in `LiteBus.Extensions.Diagnostics.HealthChecks`: `LiteBusHealthCheck` runs the `IDiagnosticCheck` probes collected on the host manifest (`LiteBusHealthCheckExtensions.AddLiteBus` registers it; see `src/LiteBus.Extensions.Diagnostics.HealthChecks/`). Telemetry ships per concern in `LiteBus.Messaging.Extensions.OpenTelemetry`, `LiteBus.Inbox.Extensions.OpenTelemetry`, `LiteBus.Outbox.Extensions.OpenTelemetry`, and `LiteBus.Transport.Extensions.OpenTelemetry` (see [Dependency Graph](../architecture/dependency-graph.md)). Backlog and state introspection live on `IOutboxManager` (`GetStatusCountsAsync`, `GetRetentionStatusAsync`, `GetSchemaInfoAsync`, `QueryAsync`). None of these are referenced by default, per the opt-in packaging rule: a read-only publisher must not transitively install health-check or telemetry dependencies.

### Options

- Reference the opt-in packages by default from broader bundles.
- Keep them opt-in and document the mapping from operational need to package.

### Recommendation

Keep the packages opt-in and document the mapping, as the integrator suggests. Widening default references would violate the opt-in packaging rule the roadmap elsewhere preserves. The documentation should map each need directly: backlog and dead-letter depth to `IOutboxManager.GetStatusCountsAsync` plus the `LiteBusHealthCheck` probe surface; dispatch latency and failure rates to the matching `*.Extensions.OpenTelemetry` package; retention behaviour to `GetRetentionStatusAsync` and `RunRetentionPurgeAsync`. A short operations recipe per need beats a meta-package that drags every SDK into every service.
