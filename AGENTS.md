# LiteBus agent instructions

Default guidance for changes in this repository. These are conventions and guardrails, not immutable law.

Package inventories, registration recipes, and feature-specific detail live in `site/content/docs/architecture/README.md`, `site/content/docs/architecture/dependency-graph.md`, and `site/content/docs/architecture/hosted-services.md`.

## How agents should use this guide

- **Treat every section as a default, not a veto.** When a task conflicts with a rule here or in `site/content/docs/`, say so plainly: what the rule expects, what the task needs, and the trade-off.
- **Propose alternatives.** Offer at least one viable path that follows the guide and, when useful, one that bends or breaks it with justification. Counter-argue your own recommendation when the trade-offs are close.
- **Override only with confirmation.** If the user accepts a deviation (forbidden role edge, skipped docs, different package shape, and similar), proceed and note the exception in the change summary. Do not silently ignore a rule.
- **Suggest guide updates.** When repeated overrides, new patterns, or outdated docs show a rule no longer fits, recommend a concrete edit to `AGENTS.md` or the relevant `site/content/docs/` file. The user decides whether to adopt it.
- **Prefer dialogue over deadlock.** A short question beats a long assumption. If confirmation is unclear, ask once with options rather than blocking on rigid compliance.

## Writing Style (documentation and prose)

Write for experienced .NET developers who already understand messaging, CQS, DDD, and dependency injection.

- Use plain ASCII punctuation. Do not use em dashes, smart quotes, emoji, or decorative symbols.
- Lead with the exact capability, then a runnable example, then constraints and explanation. Keep reference pages dense and precise.
- Describe LiteBus in technical terms: command, query, and event mediation, with optional inbox, outbox, saga, storage, transport, hosting, and observability modules.
- Prefer nouns and verbs over slogans or taglines. Headings name the capability. Use `Durable Inbox and Transactional Outbox`; avoid claims such as `Messages That Never Get Lost`.
- Do not use sales cadence, rhetorical fragments, or unsupported superlatives. State the API, execution model, durability boundary, or integration directly.
- Prefer `command, query, and event mediation` over `in-process mediator` unless process locality is the subject. When locality matters, say that handlers execute in the caller's process.
- Describe durable behavior with exact terms such as persisted inbox acceptance, transactional outbox enqueueing, lease-based processing, and the configured storage or transport adapter.
- Use CQS and DDD terminology where it clarifies API intent. Do not present those terms as branding.
- When comparing LiteBus with MediatR, lead with the licensing difference and the semantic command, query, and event model. Keep `IRequest` replacement details in migration documentation, not landing-page copy. Verify and cite current external licensing claims.
- State the LiteBus license policy directly: LiteBus is MIT licensed, open source, free for commercial use, and will remain free. Do not turn the policy into a tagline.
- Use Title Case for consumer-facing page headings, section headings, navigation labels, and primary buttons. Use sentence case for body text.
- Use exact, sourced numbers. Label illustrative examples as illustrative and do not present them as benchmarks.
- State meaningful limitations and opt-in boundaries alongside the related capability. For example, durable processing requires a selected store and processor host; mediation alone does not persist messages.

## XML documentation (required)

All C# under `src/` must use XML documentation comments (`///`) on every construct, including `private` and `internal` members. This applies to shipping libraries consumers reference and to internal implementation details agents maintain.

### What must be documented

| Construct | Required tags |
|-----------|----------------|
| Namespace | Not required (no XML on namespace declarations) |
| Type (`class`, `struct`, `record`, `interface`, `enum`, `delegate`) | `<summary>`; `<remarks>` when behavior is non-obvious |
| Public / internal / protected members | `<summary>`; `<param>` per parameter; `<returns>` when not void; `<typeparam>` per type parameter |
| Private members | `<summary>` at minimum; `<param>` / `<returns>` / `<typeparam>` when applicable |
| Private and internal fields | `<summary>` describing role and lifetime |
| Explicit interface implementations | `<inheritdoc />` or explicit `<summary>` |
| Constructors | `<summary>`; `<param>` for each parameter |
| Properties | `<summary>`; `<value>` when the meaning of the value is not obvious from the name |

### Style (match existing LiteBus packages)

- Indent summary text with four spaces after the opening tag (same as current public API docs).
- Use `<see cref="TypeName" />` for references to types and members in this solution.
- Use `<see langword="null" />`, `<see langword="true" />`, and `<see langword="false" />` where appropriate.
- Prefer complete sentences in summaries.
- Do not document auto-generated designer or assembly attribute boilerplate.
- Replace member-level `//` comments with `///` when documenting that member; keep `//` only for local algorithm notes inside method bodies.

### Examples

```csharp
/// <summary>
///     Registers a message type with a stable contract name and version.
/// </summary>
/// <typeparam name="TMessage">The concrete message type to register.</typeparam>
/// <param name="name">The stable contract name stored in persisted envelopes.</param>
/// <param name="version">The positive contract version stored with the payload.</param>
/// <returns>The registry so module builders can chain registrations.</returns>
IMessageContractRegistry Register<TMessage>(string name, int version = 1)
    where TMessage : notnull;

/// <summary>
///     Gets the message registry used to register handlers and message types.
/// </summary>
private readonly IMessageRegistry _messageRegistry;
```

```csharp
/// <summary>
///     Links newly discovered handler descriptors to committed message descriptors.
/// </summary>
/// <param name="newDescriptors">The handler descriptors to link.</param>
private void LinkHandlersToCommittedMessages(IList<IHandlerDescriptor> newDescriptors)
```

### Out of scope

- `benchmarks/` are not required to follow documentation rules unless a task explicitly says otherwise.
- Do not add XML comments that restate the identifier without adding meaning (for example, `/// <summary>Gets the count.</summary>` on `Count` is acceptable; `/// <summary>Count.</summary>` is not).
- Do not add file header blocks (`// <copyright>`, license banners). LiteBus uses per-member `///` documentation only.

### Tests and samples

- **`tests/` and `samples/`**: require XML documentation on **public** types and members in test helpers, shared fixtures, and sample host entry points. Private test methods and nested types do not require `///` unless a task says otherwise.
- StyleCop documentation warnings apply via `tests/.editorconfig` and `samples/.editorconfig`.

### Verification

After editing `src/`:

```bash
dotnet build LiteBus.slnx
```

`StyleCop.Analyzers` is referenced from `src/Directory.Build.props`. Fix documentation analyzer warnings (SA1600 through SA1629) before finishing a documentation task. File header rules (SA1633 and related) are disabled. See **Code style and analyzers** for the full analyzer inventory on `src/`, `tests/`, and `samples/`.

## Code style and analyzers

Conventions below apply during cleanup and to all new code. Analyzer severities live in `src/.editorconfig`, `tests/.editorconfig`, `samples/.editorconfig`, and ReSharper inspection keys in the root `.editorconfig`. `src/Directory.Build.props` enables `AnalysisLevel`, `AnalysisMode`, and `EnforceCodeStyleInBuild`.

### Null and argument validation (.NET 6+ helpers)

- **Always** use static throw helpers:
  - `ArgumentNullException.ThrowIfNull(...)`
  - `ArgumentException.ThrowIfNullOrWhiteSpace(...)` / `ThrowIfNullOrEmpty(...)` where applicable
  - `ArgumentOutOfRangeException.ThrowIfLessThan` / `ThrowIfNegative` for range checks
- Reserve `throw new ArgumentNullException(nameof(x))` only when a **custom message** is required.

### File layout and naming

- **One top-level type per file** (class, interface, record, struct, enum, delegate).
- **Allowed exceptions** (document explicitly):
  - `private`/`internal` nested types inside a `partial` class (for example HTTP JSON DTOs in `LiteBusManagementEndpointModels.cs`).
- **Generic filenames**: bracket arity, as in `ICommandHandler[TCommand, TCommandResult].cs`. Do not use CLR backtick names.
- **Non-generic and generic variants are separate files** (split pairs like `InboxAcceptItem` + `InboxAcceptItem<TMessage>`).

### Namespaces (required)

- **File-scoped namespaces only**: `namespace Foo.Bar;`
- Block-scoped `namespace Foo.Bar { ... }` is **not allowed** (`IDE0161` is **error** on `src/`, **warning** on `tests/` and `samples/`).

### Class construction (no primary constructors on classes)

- **Do not** use C# 12 primary constructors on `class` types. They clutter the type header and hurt readability in a public library.
- Use explicit fields/properties plus a conventional constructor body (existing LiteBus pattern).
- **Positional records** for immutable DTOs/value objects remain fine (`public sealed record Foo(string Bar);`).
- Do not introduce new primary-constructor classes during cleanup; convert any found to explicit constructors. `IDE0290` is disabled so Roslyn does not suggest primary constructors.

### Property mutability

- **Records and immutable DTOs**: `{ get; init; }` or positional records.
- **Documented exceptions** where `set` stays:
  - EF Core entity / projection types.
  - Intentionally mutable pipeline state (for example `MessageErrorContext`).
  - ASP.NET two-way binding types (rare).

### Collection expressions (required)

- Use collection expressions `[]`, `[x]`, `[..items]` instead of:
  - `Array.Empty<T>()`
  - `new List<T>()` / `new T[] { }` when building inline collections
  - `Enumerable.Empty<T>()` for empty returns
- Apply on every touched file during cleanup; grep for legacy patterns and convert.

### Collection parameters vs strongly typed collections

- Prefer **`params` read-only spans/arrays** for **variadic convenience** APIs where callers pass a small, inline list:
  - Module registration helpers (`RegisterDiagnosticCheck`, tag lists, module type lists).
  - Internal builder wiring with 2-5 homogeneous arguments.
- **Keep `IReadOnlyList<T>`** for **batch/domain writer APIs** per existing [API Design](site/content/docs/architecture/api-design.md) rules (`AcceptBatchAsync`, `EnqueueAsync` batch entries, parallel business data). Do not replace batch contracts with `params`.
- When converting, use `params ReadOnlySpan<T>` or `params T[]` and wrap to `IReadOnlyList` internally only at the public batch boundary if needed.

### Enums

- `[Flags]` only when values are combined with `|`, `&`, or `~`.
- No bitwise ops on plain enums (`CA1069`).

### Library async discipline

- All `await` in library code (`src/`, and tests/samples where async) must use `.ConfigureAwait(false)` unless documented host-context requirement (ASP.NET host-adapter edge only) (`CA2007`).

### Exception handling at transport boundaries

- **In scope**: rewrite `catch (Exception)` at transport ingress, publishers, consumers, and dispatch adapters.
- Replace with **specific catches** where the SDK documents them (`OperationCanceledException`, broker-specific exceptions, `JsonException`, and similar).
- When a broad catch is unavoidable at a boundary, catch `Exception`, **log with context**, map to a **domain transport/dispatch exception**, and never swallow silently.
- Add XML `<remarks>` on the catch block explaining why the boundary remains broad if specificity is impossible.
- `CA1031` warns on general `catch (Exception)`; suppress only with justification.

### Standard attributes (public library)

Add where missing on public surfaces touched during cleanup:

| Attribute | When |
|-----------|------|
| `[EnumeratorCancellation]` | `CancellationToken` parameter on `IAsyncEnumerable` / async-iterator methods |
| `[DebuggerDisplay]` | High-churn public value types (envelopes, receipts, key options), with concise output and no PII |
| `[DynamicallyAccessedMembers]` / `[RequiresUnreferencedCode]` | Reflection paths (`MessageRegistry`, contract resolution, saga state discovery) |
| `[EditorBrowsable(Never)]` | Advanced/internal registration hooks exposed as public for framework reasons |
| `[Obsolete(message, error: false)]` | Already used for telemetry renames; keep pattern consistent |

Do not add attributes that duplicate compiler/NRT behavior or bloat every type.

### Analyzer inventory (`src/.editorconfig`)

| Rule | ID | Severity | Purpose |
|------|-----|----------|---------|
| Use `ThrowIfNull` | CA1510 | warning | Null argument validation |
| Use `ThrowIfNullOrEmpty` | CA1511 | warning | String/collection null-or-empty |
| ConfigureAwait | CA2007 | warning | Library async discipline |
| Non-Flags enum bitwise | CA1069 | warning | Enum misuse |
| One type per file | SA1402 | warning | File layout |
| File name matches type | SA1649 | warning | Generic `[T]` naming |
| File-scoped namespace | IDE0161 | **error** | Block-scoped namespaces forbidden |
| Collection expression | IDE0300 | warning | Prefer `[]` syntax |
| Primary constructor on class | IDE0290 | **none** | Explicitly disabled |
| Catch general Exception | CA1031 | warning | Transport/boundary review |

Keep disabled: StyleCop layout/spacing categories, SA1633 file headers, IDE0007/`var` wars, CA1062 (redundant with NRT + CA1510).

### ReSharper inspections (Cursor IDE)

Root `.editorconfig` sets `resharper_*_highlighting = warning` for redundant usings, file-scoped namespaces, ConfigureAwait, collection/array style, redundant defaults, casts, `using` declarations, `this.` qualification, always-true/false conditions, and unused private members. Do not enable inspections that recommend primary constructors on classes or bulk LINQ simplifications that hurt hot-path readability.

After editing a batch, call `ReadLints` on touched paths and fix ReSharper warnings at warning severity.

## Architecture principles

### Dependency role rules

Every package belongs to exactly one dependency role. A package may reference only the project and package roles allowed by the matrix below.

**Forbidden role edges are the default failure mode in review.** Before adding a project or package reference, confirm that the target is allowed for the source role. If a feature needs a forbidden type, the usual fix is to move the contract into an allowed contract role, introduce an abstraction, or add a dedicated feature or host adapter. If none of those work, explain why and get explicit approval before merging a violation.

| Role | Purpose | Allowed dependencies |
|---|---|---|
| Platform contracts | Cross-cutting runtime and transport abstractions | Platform contracts and the BCL |
| Mediation contracts | Messaging and semantic mediator contracts | Platform and mediation contracts, plus the BCL |
| Durable contracts | Durable messaging and saga contracts | Platform, mediation, and durable contracts, plus the BCL |
| Core implementation | Default implementations without technology or host coupling | Contract roles and other core implementations |
| Technology adapter | One persistence or broker technology | Contract roles, core implementations, technology adapters, and one relevant SDK family |
| Feature bridge | Storage, dispatch, ingress, or cross-feature integration | Contract roles, core implementations, technology adapters, and relevant feature bridges |
| Host adapter | DI, hosting, OpenTelemetry, health checks, or ASP.NET Core | Applicable roles and the relevant host framework |
| Consumer tooling | Analyzers and test support | Roles and packages required by the tool |
| Aggregate | The `LiteBus` convenience package | Contract and core implementation roles only |

The current package-to-role map and exact allowlist are maintained in [Dependency Graph](site/content/docs/architecture/dependency-graph.md) and enforced for every `src/**/*.csproj` by `ArchitectureDependencyPolicyTests`.

### Package roles

| Suffix / pattern | Role | Typical dependency role |
|---|---|---|
| `*.Abstractions` | Contracts only; no concrete SDK or hosting references | Platform, mediation, or durable contracts |
| Core package (no suffix) | Default implementation for one domain concern | Core implementation |
| `*.Storage.*` | Persistence adapter | Technology adapter or feature bridge |
| `*.Dispatch.*` / `*.Ingress.*` | Execution or intake adapter | Feature bridge |
| `*.Extensions.*` | Framework or host composition adapter | Host adapter |

Name new packages to match an existing role before inventing a new shape. The aggregate `LiteBus` meta-package is the only kitchen-sink reference; all other integrations stay opt-in.

### Granular opt-in packages (intentional)

LiteBus splits NuGet packages along **concern boundaries** so consumers reference only what they run. Many packages look small (for example a single `UseAmqpDispatch` registration file); that thin surface is deliberate wiring, not an invitation to merge assemblies for fewer package IDs.

**Default rule:** preserve one installable package per orthogonal concern. Do not collapse, combine, or meta-bundle adapters unless the user or a maintainer explicitly approves a breaking packaging change.

| Split | Why it stays separate |
|---|---|
| Inbox vs outbox | Different durable semantics (accept/execute vs enqueue/publish), processors, store roles, and operational paths. Services often need one axis only; they scale and evolve on different timelines. |
| Dispatch vs ingress vs storage | Storage is persistence; dispatch is outbound execution after lease; ingress is broker intake into accept. A read-only publisher may use outbox dispatch without inbox ingress or storage on the same host. |
| Inbox.Dispatch.* vs Outbox.Dispatch.* | Same broker, different envelope contracts and mediators (`ICommandMediator` vs `IEventMediator`). Installing `Inbox.Dispatch.Amqp` must not imply `Outbox.Dispatch.Amqp` or its transitive graph. |
| Per broker (`*.Dispatch.Amqp`, `*.Ingress.Kafka`, `Transport.Amqp`) | Each broker pulls its own SDK. A Kafka-only service must not transitively reference RabbitMQ, Azure Service Bus, or AWS clients. |
| Per store (`*.Storage.PostgreSql`, `*.Storage.EntityFrameworkCore`, `*.Storage.InMemory`) | Same rule for ORM and database drivers. |
| Shared core vs broker glue (`Inbox.Dispatch` + `Inbox.Dispatch.Amqp`) | Shared dispatch logic stays broker-neutral; glue packages register one `IModule` pair without referencing sibling brokers or the other durable axis. |
| Per-module `*.Extensions.Microsoft.DependencyInjection` / `*.Extensions.Autofac` | Empty reference-only install shells; one NuGet ID per semantic module for opt-in registration. Keep these packages separate. |

**Consumer composition** follows need, not defaults:

```text
Inbox only, AMQP dispatch, PostgreSQL storage
  -> Inbox (+ abstractions via module)
  -> Inbox.Storage.PostgreSql
  -> Inbox.Dispatch.Amqp          (not Outbox.Dispatch.Amqp)
  -> optional Inbox.Ingress.Amqp  (only when consuming from a broker)

Outbox only, in-process dispatch
  -> Outbox
  -> Outbox.Storage.*
  -> Outbox.Dispatch.InProcess    (no inbox packages)
```

**What agents should not propose by default**

- Removing or merging per-module empty `*.Extensions.Microsoft.DependencyInjection` or `*.Extensions.Autofac` shell packages.
- Merging inbox and outbox adapters into one package because they share similar file names.
- A single `UseTransport(TransportKind, ...)` API backed by one assembly that references every `Transport.*` broker (forces unused SDKs onto consumers).
- "Durable" or "Transport" meta-packages that bundle both axes and multiple brokers for convenience (duplicates the forbidden kitchen-sink pattern outside the documented `LiteBus` / `Extensions.*` entry points).
- Treating high package count in `site/content/docs/architecture/dependency-graph.md` as technical debt; treat **unwanted transitive dependencies** as the debt signal instead.

**When consolidation is in scope**

- The user explicitly asks to reduce package count and accepts breaking reference changes.
- Two packages always ship together, share identical versioning constraints, and never appear independently in samples or consumer apps (rare; needs evidence).
- Extracting **shared implementation** into an allowed core or technology package without changing which packages consumers must install (refactor, not merge).

Per-module empty DI/Autofac extension shells are **not** consolidation candidates unless a maintainer explicitly requests their removal in a tracked packaging change.

Ergonomic aliases belong in **documentation and samples**, not in wider default dependency graphs. See [Dependency Graph](site/content/docs/architecture/dependency-graph.md) for the living inventory.

### Feature axes

- **Vertical** domain packages (durable messaging, saga, semantic mediators) must not reference each other or broker or ORM SDKs unless the dependency rule table in `site/content/docs/architecture/dependency-graph.md` explicitly allows it.
- **Horizontal** platform packages (runtime, transport) must not reference vertical domain abstractions.
- Mapping between axes (domain envelope to wire format, store row to contract) belongs in feature bridges, not platform core.

### Abstract package rules

Packages ending in `.Abstractions` contain only interfaces, value objects, enums, exceptions, attributes, and coordination types whose fields and parameters are abstract types. They never reference concrete implementation, storage, transport, or hosting packages.

**Abstractions stay abstract on public surfaces.** Parameters, return types, properties, and fields exposed by `*.Abstractions` types must be interfaces, primitives, enums, records, or other types from allowed contract roles. Do not surface concrete store, transport, ORM, broker SDK, or hosting types in public or internal API members consumers could depend on indirectly.

### API and value object design

Public APIs group related parameters into **semantic types named by role**, not by parameter count. Match an existing suffix before inventing a new one. Concrete inventories, package maps, feature exemplars, **CLR kind selection**, and the inbox/outbox `Message` property rule live in [API Design](site/content/docs/architecture/api-design.md); this section states the rules that apply to every axis.

#### Separate concerns by model role

| Role | Purpose | Typical suffix |
|------|---------|----------------|
| Command input | What the caller intends for one operation | `*Item`, sometimes `*Request` |
| Per-operation annotations | Metadata on a command, composed from value objects | `*Metadata` |
| Invocation tuning | How one call behaves (mediation, optional second parameter) | `*Settings` |
| Host and module configuration | Registration-time behavior for processors, stores, adapters | `*Options`, `*HostOptions` |
| Query predicate | What to filter or purge | `*Filter` |
| Persistence row | What storage holds after mapping | Envelope or entity types (sparse fields allowed) |
| Small identifier | Shared, stable value object | No suffix |

Do not merge command input, invocation tuning, and module configuration into one type.

#### Suffix taxonomy

| Suffix | Use when | Do not use for |
|--------|----------|----------------|
| `*Item` | One atomic command: payload plus per-operation metadata | Host config, parallel parameter lists |
| `*Metadata` | Annotations on an `*Item`, built from grouped value objects | Module registration, mediation tuning |
| `*Request` | Operation input without a domain payload body | Full message accept or enqueue commands |
| `*Settings` | Per-invocation pipeline tuning on mediators | Stored message metadata |
| `*Options` | Module, processor, adapter, or store configuration | Per-message writer input |
| `*HostOptions` | Background-service lifecycle only | Business commands |
| `*Filter` | Query or purge predicates where optional fields are intentional | Writer commands |
| `*Binding` | Framework adapter HTTP or host binding input at the edge | Writer commands, mediation settings |
| (no suffix) | Small identifiers and cross-cutting value objects | Bags of nullable primitives |

**Banned suffixes and shapes**

- Do not introduce `*Specification` types. In domain-driven design that suffix means a selection predicate; use plain concept names with nested variants instead.
- Do not use `*Options` for per-message or per-command writer input.
- Do not expose parallel aligned parameter lists that must stay length-matched. Use `IReadOnlyList<*Item>`.
- Do not embed `CancellationToken` inside `*Item`, `*Metadata`, or `*Request`. Pass cancellation as the final method parameter only.
- Do not add writer or store overloads with **two or more business parameters** beyond the message body once an `*Item` or `*Request` exists. Optional metadata, scheduling, routing, and identity belong on `*Item` / `*Metadata`, not on parallel scalar parameters.

#### Metadata concern categories

When a command carries optional behavior, group by **concern** inside `*Metadata` using discriminated value objects (explicit variants, not nullable "absent" fields):

| Concern | Typical variants |
|---------|------------------|
| Identity | generated vs caller-supplied identifier |
| Idempotency | none vs keyed |
| Scheduling or visibility | immediate vs at absolute time vs after delay |
| Trace or correlation | none vs correlated vs workflow vs distributed context |
| Tenancy | unscoped vs isolated tenant |
| Routing or target | default vs explicit destination (axis-specific) |

Feature packages name their own value objects. Shared cross-axis primitives belong in the narrowest contract role that both axes may reference. See [API Design](site/content/docs/architecture/api-design.md) for the durable-messaging application.

#### Optional data and mapping

- **Command boundary:** prefer abstract record hierarchies with named variants (`None`, `Generated`, `Supplied`) over nullable properties.
- **Persistence boundary:** sparse nullable columns are allowed on envelopes and store rows.
- **Query boundary:** optional predicates on `*Filter` types are allowed.
- **External input:** broker or framework payloads may be nullable; map once at the adapter edge.

One mapper per feature owns translation from command value objects to persistence shape. Adapters translate external formats into command metadata; they do not duplicate mapping logic at the store.

#### Method shape and parameter budget

- **0-2 business parameters:** inline parameters are acceptable when each is orthogonal.
- **3 or more business parameters:** introduce or extend an `*Item` or `*Request` before adding another parameter.
- **Batch operations:** accept `IReadOnlyList<*Item>`, not params arrays or parallel lists of related values.
- **Async methods:** semantic input first, `CancellationToken` last.
- **Mediators:** message plus optional `*Settings` plus `CancellationToken` remains the established pattern.

#### Role placement

- Shared value objects used by multiple axes sit in the narrowest shared `*.Abstractions` package allowed by the role matrix.
- Axis-specific command types (`*Item`, `*Metadata`, receipts) sit in that axis's `*.Abstractions`.
- Mediation `*Settings` sit in the matching semantic module abstractions package.
- Module and host `*Options` sit in the package that registers the service.
- Internal projection helpers (command to persistence) stay `internal` in the core implementation package for that feature.
- Adapters map and wire; they do not define alternate option bags for concerns already modeled in abstractions.

#### Ergonomics

- Prefer **static factories on `*Item` records** (`OutboxEnqueueItem<T>.From(message)`) so construction stays discoverable on the type callers pass to writer APIs.
- Writer facades may expose **one body-only sugar overload** per operation family (for example `EnqueueAsync<TEvent>(TEvent message, ...)` implemented as `EnqueueAsync(OutboxEnqueueItem<TEvent>.From(message), ...)`). Do not add further overloads that take metadata scalars.
- Use **`with`** on `*Item` and `*Metadata` for ad-hoc composition. Add named static helpers on the `*Item` record only when nested `with` is repetitive and domain-named (`ScheduledAt`, `WithTopic`).
- Optional thin static helper types (`OutboxEnqueue`, `InboxAccept`) are acceptable for cross-shape glue (untyped batch entries). Do not use plural `*Items` names that imply a collection type. Avoid no-op batch wrappers that only return a passed array.
- Keep compose-time `*Options` on module builders. Do not thread them through runtime command methods.
- When a workflow needs both mediation and durable persistence, use separate calls with separate semantic types; do not merge the concerns.

#### Before adding a public API

1. Is this command, configuration, query, persistence, or external adapter input?
2. If command: is there a single `*Item` or `*Request`?
3. Are optional concerns expressed as variants, not nulls?
4. Are there more than two business parameters? If yes, collapse to one semantic type.
5. Is `CancellationToken` on the method only?
6. Does mapping live in one place?
7. Does the suffix match the taxonomy table?
8. Does the type belong in abstractions or an adapter package per dependency role rules?

#### API alignment

Surfaces that do not match this taxonomy should be aligned when their area is next touched: options objects that mix invocation tuning with cancellation or strategy references; methods with three or more scalar parameters where a request record would clarify intent; error and lease APIs that pass parallel context fields instead of a context record. Specific type names, CLR kind rules (`sealed record` vs `sealed class` for `*HostOptions`), `*Binding` adapter types, and target shapes are tracked in [API Design](site/content/docs/architecture/api-design.md). Historical mappings belong in the [Migration Guides](site/content/docs/migration/README.md).

### Composite module pattern

Modules with sub-modules implement `ICompositeModule`. `DeclareChildren` runs during `Register()` before any `Build()`, and the builder action runs inside `DeclareChildren`. `Build()` registers core services only. Required ordering is expressed through `IRequires<TModule>` and composite ownership; modules do not use registration markers or scan the registry during `Build()`. The registry inserts children depth-first, validates the complete dependency graph, and then topologically sorts it before building any module. Duplicate registration of the same module type throws `LiteBusConfigurationException` at compose time.

**Compose through parent module builders.** Register storage, dispatch, and ingress inside the parent module builder via `Use*` extensions. Do not add new top-level `IModuleRegistry` shortcuts that bypass the parent builder. Mark obsolete patterns rather than extending them.

### Composition rules

- Applications reference only packages they compose. See **Granular opt-in packages** above; never widen a package reference graph because another integration exists in the same repo.
- Do not add convenience APIs on shared builders that pull storage, transport, or other adapters into generic DI packages.
- Defer ergonomic shortcuts to `site/content/docs/roadmap/README.md` when they would violate role boundaries or opt-in packaging.

### Adapter rules

- One adapter package per integration surface (one store technology, one broker, one host framework).
- Adapters register through `IModuleConfiguration`; they do not register `IHostedService`, `IHealthCheck`, or equivalent framework types directly.
- Observability and diagnostics use framework-neutral contracts or dedicated host-adapter `*.Extensions.*` packages.

## Runtime patterns

### Manifest and hosting

- One-shot host startup work implements `IStartupTask` with `Task RunAsync(CancellationToken cancellationToken)` and registers through `configuration.RegisterStartupTask(Type)`.
- Long-running host loops implement `IBackgroundService` with `Task ExecuteAsync(CancellationToken stoppingToken)` and register through `configuration.RegisterBackgroundService(Type)`.
- Modules register services with `configuration.DependencyRegistry.Register(DependencyDescriptor)`.
- Do not put `RegisterBackgroundService` or `RegisterStartupTask` on `IDependencyRegistry`. DI adapters must not reference `Microsoft.Extensions.Hosting`.
- Generic host bridging lives in `LiteBus.Runtime.Extensions.Microsoft.Hosting` and `LiteBus.Runtime.Extensions.Autofac.Hosting`, applied from `AddLiteBus` after module build.
- Diagnostic probes implement `IDiagnosticCheck` and register through `configuration.RegisterDiagnosticCheck(Type, string)`. `AddLiteBus` exposes collected descriptors on `LiteBusHostManifest`.
- **Manifest over direct host wiring.** Any work the generic host must run (startup tasks, background loops, diagnostic probes) registers through `IModuleConfiguration` manifest methods. Core and adapter packages must not register host framework types directly.

See `site/content/docs/architecture/hosted-services.md` for registration examples and feature-specific hosted types.

### Contract registration

- `IContractWriter` for module builders at configuration time.
- `IContractReader` for dispatchers and envelope factories at runtime.
- `IMessageContractRegistry` extends both and is the DI singleton key.
- `MessageContractBuilder` defers registrations until the parent module `Build()`.
- `[MessageContract]` is read at runtime via `AddFromAssembly` and on-demand in `GetContract`.

### Message registry

- `IMessageWriter` for module builders at configuration time (`Register(Type)`).
- `IMessageReader` for the mediator and resolve strategies at runtime (`Find`, enumeration, `Handlers`, `Count`).
- `IMessageRegistry` extends both and is the DI singleton key; created once per `IModuleConfiguration` in `MessageModule.Build()` via `GetOrCreateContext`.
- Do not use a process-wide static accessor or `Clear()`; tests use a new `MessageRegistry` instance per case.

## Public contract stability

- Telemetry meter names, activity source names, and instrument name constants on public telemetry types are part of the consumer contract. Treat renames and removals as breaking changes.
- When adding instruments, define names as public `const string` on the telemetry type, register meters through the matching host-adapter `*.Extensions.OpenTelemetry` package, and document new names in `site/content/docs/architecture/README.md`.
- Builder method renames, manifest entry changes, and persisted envelope field semantics are breaking; update docs in the same change.
- Prefer stable contract names and versions over assembly-qualified CLR names in persisted envelopes.

## Package and framework dependencies

Keep each package's dependency graph minimal and aligned with its dependency role. Before adding a NuGet or project reference, confirm the consuming code uses a type or member that truly requires it.

- **Prefer BCL and abstractions already in the graph.** `IServiceProvider.GetService(Type)` lives in `System`; do not add `Microsoft.Extensions.DependencyInjection*` packages only to call `GetService<T>()` or other extension methods. Use the non-generic API or inject the required service through constructors and module registration instead.
- **Restrict hosting and framework packages to host adapters.** Other roles must not reference `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Diagnostics.HealthChecks`, ASP.NET Core, or similar host frameworks.
- **One integration surface per concern.** Observability, health, and diagnostics belong in framework-neutral contracts (`IDiagnosticCheck`, OpenTelemetry meters on public constants) or in dedicated `*.Extensions.*` adapter packages.
- **Justify every new reference in review.** If a feature can be expressed with an existing abstraction, manifest entry, or documented application code, prefer that over a new package dependency.

## New package checklist

Before adding a project:

1. Which dependency role and role suffix?
2. Can an existing package absorb this without a forbidden role edge?
3. Does it need a new `*.Abstractions` package or fit an existing one?
4. Does it need manifest registration (startup task, background service, diagnostic check)?
5. Does `site/content/docs/architecture/dependency-graph.md` need a new row?
6. Does `site/content/docs/architecture/README.md` need a feature section or invariant note?

## Analyzers

- Ship compile-time rules in `LiteBus.Analyzers` only; no runtime dependency on mediator or durable packages.
- Keep the rule inventory in `site/content/docs/reference/analyzers.md` aligned with `DiagnosticIds` (LB1001-LB1017).
- **LB1007** covers handled durable types missing contract registration; honor `RegisterFromAssembly` the same as explicit `Contracts.Register`.
- **LB1017** covers attributed durable types; match only `IContractWriter` / `IMessageContractRegistry` `Register` invocations, not unrelated `Register<T>()` methods.
- **LB1004** must cover `AcceptAsync`, `AcceptBatchAsync`, and `ITransactionalInbox` acceptance APIs.
- Add or update analyzer tests in `tests/LiteBus.Analyzers.Tests` when changing diagnostic behavior.

## Build and repo hygiene

- Run `dotnet build LiteBus.slnx` after `src/` edits.
- Strong-named assemblies: `InternalsVisibleTo` must include the solution public key (see `src/Directory.Build.props`).
- Central package versions: add NuGet packages to `src/Directory.Packages.props`, not inline in csproj files.
- New test projects using xUnit attributes: add `GlobalUsings.cs` with `global using Xunit;` when the project has no other global usings for xUnit.

## Testing

- **Tests prove behavior, not wiring.** Prefer assertions on outcomes (manifest contents, probe results, processor pass behavior, store state transitions) over tests that only verify a type appears in dependency injection or a module builds without exception.
- Use a fresh `MessageRegistry` (or isolated module configuration) per test case; do not rely on process-wide static state.
- Add regression tests when fixing manifest ordering, diagnostic registration, telemetry recording, or composite module child expansion.

## Documentation

- **Docs move with API changes.** When adding, renaming, or removing public builder methods, contracts, manifest entries, metric names, or registration patterns, update the matching section in `site/content/docs/` in the same change (`Architecture.md`, `API-Design.md`, `Hosted-services.md`, feature guides, or `Roadmap.md` when scope shifts).
- Document application-owned integration (health endpoints, schema probes, export sinks) as recipes; ship framework-neutral contracts and stable telemetry names in libraries.
- Keep `site/content/docs/architecture/hosted-services.md` and `site/content/docs/architecture/README.md` aligned with the manifest model (`IStartupTask`, `IBackgroundService`, `IDiagnosticCheck`, `LiteBusHostManifest`).
- Keep `site/content/docs/architecture/dependency-graph.md` as the living package inventory and dependency rule reference.
- **The documentation is also served as plain text for agents.** `/llms.txt`, `/llms-full.txt`, and a `.md` endpoint per page are generated from the same Markdown and the same page tree the navigation uses, so a page added to a `meta.json` reaches them with no separate edit. Titles and descriptions are derived from each page's prose, so a page that opens with neither an intro paragraph, a `Summary` metadata field, nor a `## Summary` section reaches the index with no note and fails `site/scripts/check-llms.mjs`. The hand-written parts, the summary and the `Start Here` list, are in `site/lib/llms.ts`; `site/README.md` documents the endpoints.
- **`site/content/docs/` is the version under development; `site/content/versions/<id>/` is a released line.** A snapshot is corrected, not evolved: fixing an error in it is right, and rewriting it to describe a later version is not, because it would make the documentation disagree with the release it documents. While a line still receives patches, its fix lands on the branch named by that version's `tracks` in `site/versions.json`, and the snapshot has to be updated to match; `scripts/Test-VersionSnapshots.ps1` reports when it has fallen behind. The repository documentation gates deliberately scan only `site/content/docs/`, because a snapshot's snippets reference source that has since moved.

## Evolving this guide

These instructions and `site/content/docs/` should reflect how the codebase is actually built. Agents and maintainers may change any rule when the project direction shifts. Propose amendments in the same PR or conversation as the code change that motivates them, so guidance and implementation stay aligned.
