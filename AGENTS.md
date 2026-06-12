# LiteBus agent instructions

Default guidance for changes in this repository. These are conventions and guardrails, not immutable law.

Package inventories, registration recipes, and feature-specific detail live in `docs/Architecture.md`, `docs/Dependency-Graph.md`, and `docs/Hosted-services.md`.

## How agents should use this guide

- **Treat every section as a default, not a veto.** When a task conflicts with a rule here or in `docs/`, say so plainly: what the rule expects, what the task needs, and the trade-off.
- **Propose alternatives.** Offer at least one viable path that follows the guide and, when useful, one that bends or breaks it with justification. Counter-argue your own recommendation when the trade-offs are close.
- **Override only with confirmation.** If the user accepts a deviation (layer violation, skipped docs, different package shape, and similar), proceed and note the exception in the change summary. Do not silently ignore a rule.
- **Suggest guide updates.** When repeated overrides, new patterns, or outdated docs show a rule no longer fits, recommend a concrete edit to `AGENTS.md` or the relevant `docs/` file. The user decides whether to adopt it.
- **Prefer dialogue over deadlock.** A short question beats a long assumption. If confirmation is unclear, ask once with options rather than blocking on rigid compliance.

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

- `tests/`, `samples/`, and `benchmarks/` are not required to follow this rule unless a task explicitly says otherwise.
- Do not add XML comments that restate the identifier without adding meaning (for example, `/// <summary>Gets the count.</summary>` on `Count` is acceptable; `/// <summary>Count.</summary>` is not).
- Do not add file header blocks (`// <copyright>`, license banners). LiteBus uses per-member `///` documentation only.

### Verification

After editing `src/`:

```bash
dotnet build LiteBus.slnx
```

`StyleCop.Analyzers` is referenced from `src/Directory.Build.props`. Only **documentation** rule categories are warnings (`src/.editorconfig`); other StyleCop categories are disabled so layout and naming rules do not churn existing code. Fix documentation analyzer warnings (SA1600 through SA1629) before finishing a documentation task. File header rules (SA1633 and related) are disabled.

## Architecture principles

### Layer rules

Every package belongs to exactly one layer. A package may only reference packages in the same layer or layers strictly below it.

**Layer violations are the default failure mode in review.** Before adding a project or package reference, confirm the target sits in the same layer or a strictly lower layer. If a feature needs a higher-layer type, the usual fix is to move code down, introduce an abstraction in a lower layer, or add a dedicated adapter package at layer 4 or 5. If none of those work, explain why and get explicit approval before merging a violation.

| Layer | Number | Role |
|---|---|---|
| Platform contracts | 0 | Cross-cutting abstractions usable by any feature axis |
| Domain abstractions | 1 | Contracts for one vertical concern (messaging, durable messaging, saga, and similar) |
| Core implementations | 2 | Default implementations; broker or SDK adapters that implement platform abstractions |
| Shared storage infrastructure | 3 | Storage primitives reused by multiple storage adapters |
| Integration adapters | 4 | Optional persistence, dispatch, ingress, or store bindings |
| Hosting / composition | 5 | DI, generic host, OpenTelemetry, ASP.NET, and other framework bridges |

The current package-to-layer map is maintained in [Dependency Graph](docs/Dependency-Graph.md).

### Package roles

| Suffix / pattern | Role | Typical layer |
|---|---|---|
| `*.Abstractions` | Contracts only; no concrete SDK or hosting references | 0–1 |
| Core package (no suffix) | Default implementation for one domain concern | 2 |
| `*.Storage.*` | Persistence adapter | 4 |
| `*.Dispatch.*` / `*.Ingress.*` | Execution or intake adapter | 4 |
| `*.Extensions.*` | Framework or host composition adapter | 5 |

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

- Merging inbox and outbox adapters into one package because they share similar file names.
- A single `UseTransport(TransportKind, …)` API backed by one assembly that references every `Transport.*` broker (forces unused SDKs onto consumers).
- “Durable” or “Transport” meta-packages that bundle both axes and multiple brokers for convenience (duplicates the forbidden kitchen-sink pattern outside the documented `LiteBus` / `Extensions.*` entry points).
- Treating high package count in `docs/Dependency-Graph.md` as technical debt; treat **unwanted transitive dependencies** as the debt signal instead.

**When consolidation is in scope**

- The user explicitly asks to reduce package count and accepts breaking reference changes.
- Two packages always ship together, share identical versioning constraints, and never appear independently in samples or consumer apps (rare; needs evidence).
- Extracting **shared implementation** into a lower-layer package without changing which packages consumers must install (refactor, not merge).

Ergonomic aliases belong in **documentation and samples**, not in wider default dependency graphs. See [Dependency Graph](docs/Dependency-Graph.md) for the living inventory.

### Feature axes

- **Vertical** domain packages (durable messaging, saga, semantic mediators) must not reference each other or broker or ORM SDKs unless the dependency rule table in `docs/Dependency-Graph.md` explicitly allows it.
- **Horizontal** platform packages (runtime, transport) must not reference vertical domain abstractions.
- Mapping between axes (domain envelope to wire format, store row to contract) belongs in layer-4 adapters, not platform core.

### Abstract package rules

Packages ending in `.Abstractions` contain only interfaces, value objects, enums, exceptions, attributes, and coordination types whose fields and parameters are abstract types. They never reference concrete implementation, storage, transport, or hosting packages.

**Abstractions stay abstract on public surfaces.** Parameters, return types, properties, and fields exposed by `*.Abstractions` types must be interfaces, primitives, enums, records, or other abstractions from allowed lower layers. Do not surface concrete store, transport, ORM, broker SDK, or hosting types in public or internal API members consumers could depend on indirectly.

### API and value object design

Public APIs group related parameters into **semantic types named by role**, not by parameter count. Match an existing suffix before inventing a new one. Concrete inventories, package maps, feature exemplars, **CLR kind selection**, and the inbox/outbox `Message` property rule live in [API Design](docs/API-Design.md); this section states the rules that apply to every axis.

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

Feature packages name their own value objects. Shared cross-axis primitives belong in the lowest abstractions layer that both axes reference. See [API Design](docs/API-Design.md) for the durable-messaging application.

#### Optional data and mapping

- **Command boundary:** prefer abstract record hierarchies with named variants (`None`, `Generated`, `Supplied`) over nullable properties.
- **Persistence boundary:** sparse nullable columns are allowed on envelopes and store rows.
- **Query boundary:** optional predicates on `*Filter` types are allowed.
- **External input:** broker or framework payloads may be nullable; map once at the adapter edge.

One mapper per feature owns translation from command value objects to persistence shape. Adapters translate external formats into command metadata; they do not duplicate mapping logic at the store.

#### Method shape and parameter budget

- **0–2 business parameters:** inline parameters are acceptable when each is orthogonal.
- **3 or more business parameters:** introduce or extend an `*Item` or `*Request` before adding another parameter.
- **Batch operations:** accept `IReadOnlyList<*Item>`, not params arrays or parallel lists of related values.
- **Async methods:** semantic input first, `CancellationToken` last.
- **Mediators:** message plus optional `*Settings` plus `CancellationToken` remains the established pattern.

#### Layer placement

- Shared value objects used by multiple axes sit in the lowest shared `*.Abstractions` package for that concern.
- Axis-specific command types (`*Item`, `*Metadata`, receipts) sit in that axis's `*.Abstractions`.
- Mediation `*Settings` sit in the matching semantic module abstractions package.
- Module and host `*Options` sit in the package that registers the service.
- Internal projection helpers (command to persistence) stay `internal` in the core implementation package for that feature.
- Adapters map and wire; they do not define alternate option bags for concerns already modeled in abstractions.

#### Ergonomics

- Prefer **static factories on `*Item` records** (`OutboxEnqueueItem<T>.From(message)`) so construction stays discoverable on the type callers pass to writer APIs.
- Writer facades may expose **one body-only sugar overload** per operation family (for example `EnqueueAsync<TEvent>(TEvent message, …)` implemented as `EnqueueAsync(OutboxEnqueueItem<TEvent>.From(message), …)`). Do not add further overloads that take metadata scalars.
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
8. Does the type belong in abstractions or an adapter package per layer rules?

#### Legacy alignment

Surfaces that predate this taxonomy should be aligned when their area is next touched: options objects that mix invocation tuning with cancellation or strategy references; methods with three or more scalar parameters where a request record would clarify intent; error and lease APIs that pass parallel context fields instead of a context record. Specific type names, CLR kind rules (`sealed record` vs `sealed class` for `*HostOptions`), `*Binding` adapter types, and target shapes are tracked in [API Design](docs/API-Design.md) and [Migration guide v6](docs/Migration-Guide-v6.md).

### Composite module pattern

Modules with sub-modules implement `ICompositeModule`. `DeclareChildren` runs during `Register()` before any `Build()`. The builder action runs inside `DeclareChildren`. `Build()` registers core services only. Sub-modules check for a parent context marker as their first `Build()` operation. The registry inserts children depth-first after the parent, then topological sort runs. Duplicate registration of the same module type is a silent no-op.

**Compose through parent module builders.** Register storage, dispatch, and ingress inside the parent module builder via `Use*` extensions. Do not add new top-level `IModuleRegistry` shortcuts that bypass the parent builder or skip context markers. Mark obsolete patterns rather than extending them.

### Composition rules

- Applications reference only packages they compose. See **Granular opt-in packages** above; never widen a package reference graph because another integration exists in the same repo.
- Do not add convenience APIs on shared builders that pull storage, transport, or other adapters into generic DI packages.
- Defer ergonomic shortcuts to `docs/Roadmap.md` when they would violate layer boundaries or opt-in packaging.

### Adapter rules

- One adapter package per integration surface (one store technology, one broker, one host framework).
- Adapters register through `IModuleConfiguration`; they do not register `IHostedService`, `IHealthCheck`, or equivalent framework types directly.
- Observability and diagnostics use framework-neutral contracts or dedicated `*.Extensions.*` packages at layer 5.

## Runtime patterns

### Manifest and hosting

- One-shot host startup work implements `IStartupTask` with `Task RunAsync(CancellationToken cancellationToken)` and registers through `configuration.RegisterStartupTask(Type)`.
- Long-running host loops implement `IBackgroundService` with `Task ExecuteAsync(CancellationToken stoppingToken)` and register through `configuration.RegisterBackgroundService(Type)`.
- Modules register services with `configuration.DependencyRegistry.Register(DependencyDescriptor)`.
- Do not put `RegisterBackgroundService` or `RegisterStartupTask` on `IDependencyRegistry`. DI adapters must not reference `Microsoft.Extensions.Hosting`.
- Generic host bridging lives in `LiteBus.Runtime.Extensions.Microsoft.Hosting` and `LiteBus.Runtime.Extensions.Autofac.Hosting`, applied from `AddLiteBus` after module build.
- Diagnostic probes implement `IDiagnosticCheck` and register through `configuration.RegisterDiagnosticCheck(Type, string)`. `AddLiteBus` exposes collected descriptors on `LiteBusHostManifest`.
- **Manifest over direct host wiring.** Any work the generic host must run (startup tasks, background loops, diagnostic probes) registers through `IModuleConfiguration` manifest methods. Core and adapter packages must not register host framework types directly.

See `docs/Hosted-services.md` for registration examples and feature-specific hosted types.

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
- When adding instruments, define names as public `const string` on the telemetry type, register meters through the matching `*.Extensions.OpenTelemetry` package at layer 5, and document new names in `docs/Architecture.md`.
- Builder method renames, manifest entry changes, and persisted envelope field semantics are breaking; update docs in the same change.
- Prefer stable contract names and versions over assembly-qualified CLR names in persisted envelopes.

## Package and framework dependencies

Keep each package's dependency graph minimal and aligned with its layer. Before adding a NuGet or project reference, confirm the consuming code uses a type or member that truly requires it.

- **Prefer BCL and abstractions already in the graph.** `IServiceProvider.GetService(Type)` lives in `System`; do not add `Microsoft.Extensions.DependencyInjection*` packages only to call `GetService<T>()` or other extension methods. Use the non-generic API or inject the required service through constructors and module registration instead.
- **Restrict hosting and framework packages to hosting adapters.** Packages in layers 0–4 must not reference `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Diagnostics.HealthChecks`, ASP.NET Core, or similar host frameworks unless the package is an explicit hosting or composition adapter (layer 5).
- **One integration surface per concern.** Observability, health, and diagnostics belong in framework-neutral contracts (`IDiagnosticCheck`, OpenTelemetry meters on public constants) or in dedicated `*.Extensions.*` adapter packages.
- **Justify every new reference in review.** If a feature can be expressed with an existing abstraction, manifest entry, or documented application code, prefer that over a new package dependency.

## New package checklist

Before adding a project:

1. Which layer and which role suffix?
2. Can an existing package absorb this without a layer violation?
3. Does it need a new `*.Abstractions` package or fit an existing one?
4. Does it need manifest registration (startup task, background service, diagnostic check)?
5. Does `docs/Dependency-Graph.md` need a new row?
6. Does `docs/Architecture.md` need a feature section or invariant note?

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

- **Docs move with API changes.** When adding, renaming, or removing public builder methods, contracts, manifest entries, metric names, or registration patterns, update the matching section in `docs/` in the same change (`Architecture.md`, `API-Design.md`, `Hosted-services.md`, feature guides, or `Roadmap.md` when scope shifts).
- Document application-owned integration (health endpoints, schema probes, export sinks) as recipes; ship framework-neutral contracts and stable telemetry names in libraries.
- Keep `docs/Hosted-services.md` and `docs/Architecture.md` aligned with the manifest model (`IStartupTask`, `IBackgroundService`, `IDiagnosticCheck`, `LiteBusHostManifest`).
- Keep `docs/Dependency-Graph.md` as the living package inventory and dependency rule reference.

## Evolving this guide

These instructions and `docs/` should reflect how the codebase is actually built. Agents and maintainers may change any rule when the project direction shifts. Propose amendments in the same PR or conversation as the code change that motivates them, so guidance and implementation stay aligned.
