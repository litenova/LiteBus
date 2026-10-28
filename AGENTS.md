# LiteBus agent instructions

This guide tells coding agents how to change the code in this repository. Its rules are conventions and guardrails, not fixed law.

Find package inventories, registration recipes, and feature-specific detail in `site/content/docs/architecture/README.md`, `site/content/docs/architecture/dependency-graph.md`, and `site/content/docs/architecture/hosted-services.md`.

## How agents should use this guide

- Treat every section as a default, not a veto. When a task conflicts with a rule here or in `site/content/docs/`, state what the rule expects, what the task needs, and what the trade-off is.
- Propose at least one workable path that follows the guide. When breaking a rule would serve the task better, propose that path too and justify it. When the trade-offs are close, argue against your own recommendation as well.
- Override a rule only after the user confirms the deviation. If the user accepts a change such as a forbidden role edge, skipped docs, or a different package shape, proceed and note the exception in the change summary. Never ignore a rule without saying so.
- Suggest a guide update when a rule no longer fits. When repeated overrides, new patterns, or outdated docs show that, recommend a concrete edit to `AGENTS.md` or to the `site/content/docs/` file that holds the rule. The user decides whether to adopt it.
- Ask a short question instead of acting on a long assumption. If confirmation is unclear, ask once with options rather than blocking on strict compliance.

## XML documentation (required)

Document every C# construct under `src/` with XML documentation comments (`///`), including `private` and `internal` members. This rule covers both the shipping libraries that consumers reference and the internal implementation details that agents maintain.

### What must be documented

| Construct | Required tags |
|-----------|----------------|
| Namespace | Not required (no XML on namespace declarations) |
| Type (`class`, `struct`, `record`, `interface`, `enum`, `delegate`) | `<summary>`; add `<remarks>` when the behavior is not obvious |
| Public / internal / protected members | `<summary>`; `<param>` per parameter; `<returns>` when not void; `<typeparam>` per type parameter |
| Private members | `<summary>` at minimum; add `<param>` / `<returns>` / `<typeparam>` when the member has parameters, a return value, or type parameters |
| Private and internal fields | `<summary>` that describes the role and lifetime |
| Explicit interface implementations | `<inheritdoc />` or an explicit `<summary>` |
| Constructors | `<summary>`; `<param>` for each parameter |
| Properties | `<summary>`; `<value>` when the meaning of the value is not obvious from the name |

### Style (match existing LiteBus packages)

- Indent summary text by four spaces after the opening tag, as the current public API docs do.
- Use `<see cref="TypeName" />` for references to types and members in this solution.
- Use `<see langword="null" />`, `<see langword="true" />`, and `<see langword="false" />` when the text refers to those values.
- Write summaries as complete sentences.
- Do not document auto-generated designer or assembly attribute boilerplate.
- Replace a member-level `//` comment with `///` when the comment documents that member. Keep `//` only for notes about the algorithm inside method bodies.

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

- Unless a task explicitly says otherwise, `benchmarks/` code does not need to follow documentation rules.
- Do not add an XML comment that restates the identifier without adding meaning. For example, `/// <summary>Gets the count.</summary>` on `Count` is acceptable, but `/// <summary>Count.</summary>` is not.
- Do not add file header blocks such as `// <copyright>` or license banners. LiteBus uses per-member `///` documentation only.

### Tests and samples

- Document `tests/` and `samples/` code with XML comments on public types and members in test helpers, shared fixtures, and sample host entry points. Private test methods and nested types do not need `///` unless a task says otherwise.
- The `tests/.editorconfig` and `samples/.editorconfig` files enable StyleCop documentation warnings.

### Verification

After editing `src/`, run this build:

```bash
dotnet build LiteBus.slnx
```

`src/Directory.Build.props` references `StyleCop.Analyzers`. Fix documentation analyzer warnings (SA1600 through SA1629) before finishing a documentation task. File header rules (SA1633 and related) stay disabled. See Code style and analyzers for the full analyzer inventory on `src/`, `tests/`, and `samples/`.

## Code style and analyzers

Follow these conventions during cleanup and in all new code. The `src/.editorconfig`, `tests/.editorconfig`, and `samples/.editorconfig` files set analyzer severities, and the root `.editorconfig` sets ReSharper inspection keys. `src/Directory.Build.props` enables `AnalysisLevel`, `AnalysisMode`, and `EnforceCodeStyleInBuild`.

### Null and argument validation (.NET 6+ helpers)

- Always use the static throw helpers:
  - `ArgumentNullException.ThrowIfNull(...)`
  - `ArgumentException.ThrowIfNullOrWhiteSpace(...)` / `ThrowIfNullOrEmpty(...)` where applicable
  - `ArgumentOutOfRangeException.ThrowIfLessThan` / `ThrowIfNegative` for range checks
- Reserve `throw new ArgumentNullException(nameof(x))` for cases that need a custom message.

### File layout and naming

- Put one top-level type in each file (class, interface, record, struct, enum, delegate).
- Document explicitly each case where an exception is allowed:
  - A `private` or `internal` type nested inside a `partial` class (for example the HTTP JSON DTOs in `LiteBusManagementEndpointModels.cs`).
- Name generic files with bracket arity, as in `ICommandHandler[TCommand, TCommandResult].cs`. Do not use CLR backtick names.
- Keep non-generic and generic variants in separate files (split pairs like `InboxAcceptItem` + `InboxAcceptItem<TMessage>`).

### Namespaces (required)

- Use file-scoped namespaces only, as in `namespace Foo.Bar;`
- Do not use block-scoped namespaces such as `namespace Foo.Bar { ... }`. `IDE0161` reports them as **error** on `src/` and as **warning** on `tests/` and `samples/`.

### Class construction (no primary constructors on classes)

- Do not use C# 12 primary constructors on `class` types. They clutter the type header and hurt readability in a public library.
- Use explicit fields and properties with a conventional constructor body, which is the existing LiteBus pattern.
- Positional records stay fine for immutable DTOs and value objects, as in `public sealed record Foo(string Bar);`.
- Do not introduce new primary-constructor classes during cleanup. Convert any you find to explicit constructors. `IDE0290` is disabled so Roslyn does not suggest primary constructors.

### Property mutability

- In records and immutable DTOs, use `{ get; init; }` or positional records.
- Keep `set` only in these documented exceptions:
  - EF Core entity and projection types.
  - Pipeline state that is intentionally mutable (for example `MessageErrorContext`).
  - ASP.NET two-way binding types, which are rare.

### Collection expressions (required)

- Use collection expressions `[]`, `[x]`, `[..items]` instead of:
  - `Array.Empty<T>()`
  - `new List<T>()` / `new T[] { }` when building inline collections
  - `Enumerable.Empty<T>()` for empty returns
- During cleanup, apply this to every file you touch. Search for legacy patterns and convert them.

### Collection parameters vs strongly typed collections

- Prefer `params` read-only spans or arrays for convenience APIs where callers pass a small, inline list:
  - Module registration helpers such as `RegisterDiagnosticCheck`, tag lists, and module type lists.
  - Internal builder wiring that takes 2 to 5 arguments of the same type.
- Keep `IReadOnlyList<T>` for batch and domain writer APIs, following the [API Design](site/content/docs/architecture/api-design.md) rules. This covers `AcceptBatchAsync`, `EnqueueAsync` batch entries, and parallel business data. Do not replace batch contracts with `params`.
- When converting, use `params ReadOnlySpan<T>` or `params T[]`. Wrap to `IReadOnlyList` inside the method only at the public batch boundary, and only if needed.

### Enums

- Apply `[Flags]` only when code combines the values with `|`, `&`, or `~`.
- Do not use bitwise operations on plain enums. `CA1069` reports them.

### Library async discipline

- Every `await` in `src/` library code must use `.ConfigureAwait(false)`. The same applies to async code in tests and samples. The only exception is a documented host-context requirement at the ASP.NET host-adapter edge. `CA2007` enforces this.

### Exception handling at transport boundaries

- Rewrite `catch (Exception)` at transport ingress, publishers, consumers, and dispatch adapters.
- Replace the broad catch with specific catches where the SDK documents them, such as `OperationCanceledException`, broker-specific exceptions, `JsonException`, and similar.
- When a broad catch is unavoidable at a boundary, catch `Exception`, log it with context, and map it to a domain transport or dispatch exception. Never swallow the exception silently.
- Add an XML `<remarks>` note on the catch block that explains why the boundary stays broad when a specific catch is impossible.
- `CA1031` warns on a general `catch (Exception)`. Suppress it only with justification.

### Standard attributes (public library)

Add these attributes where they are missing on public surfaces you touch during cleanup:

| Attribute | When |
|-----------|------|
| `[EnumeratorCancellation]` | `CancellationToken` parameter on `IAsyncEnumerable` or async-iterator methods |
| `[DebuggerDisplay]` | Public value types with high churn (envelopes, receipts, key options), with concise output and no PII |
| `[DynamicallyAccessedMembers]` or `[RequiresUnreferencedCode]` | Reflection paths (`MessageRegistry`, contract resolution, saga state discovery) |
| `[EditorBrowsable(Never)]` | Advanced or internal registration hooks exposed as public for framework reasons |
| `[Obsolete(message, error: false)]` | Telemetry renames use this; keep the same pattern |

Do not add attributes that duplicate compiler or nullable-reference behavior, or that appear on every type without adding value.

### Analyzer inventory (`src/.editorconfig`)

| Rule | ID | Severity | Purpose |
|------|-----|----------|---------|
| Use `ThrowIfNull` | CA1510 | warning | Null argument validation |
| Use `ThrowIfNullOrEmpty` | CA1511 | warning | String/collection null-or-empty |
| ConfigureAwait | CA2007 | warning | Library async discipline |
| Non-Flags enum bitwise | CA1069 | warning | Enum misuse |
| One type per file | SA1402 | warning | File layout |
| File name matches type | SA1649 | warning | Generic `[T]` naming |
| File-scoped namespace | IDE0161 | **error** | Block-scoped namespaces are forbidden |
| Collection expression | IDE0300 | warning | Prefer `[]` syntax |
| Primary constructor on class | IDE0290 | **none** | Explicitly disabled |
| Catch general Exception | CA1031 | warning | Transport/boundary review |

Keep the following disabled: StyleCop layout and spacing categories, SA1633 file headers, IDE0007 and `var` usage debates, and CA1062, which is redundant with nullable-reference checks and CA1510.

### ReSharper inspections (Cursor IDE)

The root `.editorconfig` sets `resharper_*_highlighting = warning` for redundant usings, file-scoped namespaces, ConfigureAwait, collection and array style, redundant defaults, casts, `using` declarations, `this.` qualification, always-true and always-false conditions, and unused private members. Do not enable inspections that recommend primary constructors on classes or bulk LINQ simplifications that hurt hot-path readability.

After editing a batch of files, call `ReadLints` on the paths you touched and fix the ReSharper warnings reported at warning severity.

## Architecture principles

### Dependency role rules

Every package belongs to exactly one dependency role. A package may reference only the project and package roles allowed by the matrix below.

Forbidden role edges are the usual reason reviews fail. Before adding a project or package reference, confirm that the target is allowed for the source role. If a feature needs a forbidden type, move the contract into an allowed contract role, introduce an abstraction, or add a dedicated feature or host adapter. If none of those work, explain why and get explicit approval before merging the violation.

| Role | Purpose | Allowed dependencies |
|---|---|---|
| Platform contracts | Cross-cutting runtime and transport abstractions | Platform contracts and the BCL |
| Mediation contracts | Messaging and semantic mediator contracts | Platform and mediation contracts, and the BCL |
| Durable contracts | Durable messaging and saga contracts | Platform, mediation, and durable contracts, and the BCL |
| Core implementation | Default implementations without technology or host coupling | Contract roles and other core implementations |
| Technology adapter | One persistence or broker technology | Contract roles, core implementations, technology adapters, and the one SDK family for its technology |
| Feature bridge | Storage, dispatch, ingress, or cross-feature integration | Contract roles, core implementations, technology adapters, and the feature bridges it integrates |
| Host adapter | DI, hosting, OpenTelemetry, health checks, or ASP.NET Core | The roles it composes and its host framework |
| Consumer tooling | Analyzers and test support | Roles and packages required by the tool |
| Aggregate | The `LiteBus` convenience package | Contract and core implementation roles only |

The [Dependency Graph](site/content/docs/architecture/dependency-graph.md) lists the current package-to-role map and exact allowlist. For every `src/**/*.csproj`, `ArchitectureDependencyPolicyTests` enforces them.

### Package roles

| Suffix / pattern | Role | Typical dependency role |
|---|---|---|
| `*.Abstractions` | Contracts only, with no concrete SDK or hosting references | Platform, mediation, or durable contracts |
| Core package (no suffix) | Default implementation for one domain concern | Core implementation |
| `*.Storage.*` | Persistence adapter | Technology adapter or feature bridge |
| `*.Dispatch.*` / `*.Ingress.*` | Execution or intake adapter | Feature bridge |
| `*.Extensions.*` | Framework or host composition adapter | Host adapter |

Name new packages to match an existing role before inventing a new shape. The aggregate `LiteBus` meta-package is the only kitchen-sink reference. All other integrations stay opt-in.

### Granular opt-in packages (intentional)

LiteBus splits NuGet packages along **concern boundaries** so consumers reference only what they run. Many packages look small, for example a single `UseAmqpDispatch` registration file, but that thin surface is deliberate wiring. It is not an invitation to merge assemblies for fewer package IDs.

Preserve one installable package per orthogonal concern. Do not collapse, combine, or meta-bundle adapters unless the user or a maintainer explicitly approves a breaking packaging change.

| Split | Why it stays separate |
|---|---|
| Inbox vs outbox | Inbox and outbox have different durable semantics (accept and execute compared with enqueue and publish), processors, store roles, and operational paths. Services often need only one axis, and the two axes scale and evolve on different timelines. |
| Dispatch vs ingress vs storage | Storage holds data, dispatch executes outbound messages after lease, and ingress takes broker messages into accept. A read-only publisher may use outbox dispatch without inbox ingress or storage on the same host. |
| Inbox.Dispatch.* vs Outbox.Dispatch.* | Both use the same broker with different envelope contracts and mediators (`ICommandMediator` and `IEventMediator`). Installing `Inbox.Dispatch.Amqp` must not require `Outbox.Dispatch.Amqp` or its transitive graph. |
| Per broker (`*.Dispatch.Amqp`, `*.Ingress.Kafka`, `Transport.Amqp`) | Each broker depends on its own SDK. A Kafka-only service must not transitively reference RabbitMQ, Azure Service Bus, or AWS clients. |
| Per store (`*.Storage.PostgreSql`, `*.Storage.EntityFrameworkCore`, `*.Storage.InMemory`) | The same rule applies to ORM and database drivers. |
| Shared core vs broker glue (`Inbox.Dispatch` + `Inbox.Dispatch.Amqp`) | Shared dispatch logic stays broker-neutral. Glue packages register one `IModule` pair without referencing sibling brokers or the other durable axis. |
| Per-module `*.Extensions.Microsoft.DependencyInjection` or `*.Extensions.Autofac` | These are empty reference-only install shells, with one NuGet ID per semantic module for opt-in registration. Keep these packages separate. |

Compose consumers by need, not by defaults, as in these examples:

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

Do not propose any of the following by default:

- Do not remove or merge the per-module empty `*.Extensions.Microsoft.DependencyInjection` or `*.Extensions.Autofac` shell packages.
- Do not merge inbox and outbox adapters into one package because they share similar file names.
- Do not build a single `UseTransport(TransportKind, ...)` API backed by one assembly that references every `Transport.*` broker, because it forces unused SDKs onto consumers.
- Do not build "Durable" or "Transport" meta-packages that bundle both axes and multiple brokers for convenience, because they repeat the forbidden kitchen-sink pattern outside the documented `LiteBus` and `Extensions.*` entry points.
- Do not treat a high package count in `site/content/docs/architecture/dependency-graph.md` as technical debt. Treat **unwanted transitive dependencies** as the debt signal instead.

Consolidation is in scope in these cases:

- The user explicitly asks to reduce package count and accepts breaking reference changes.
- Two packages always ship together, share identical versioning constraints, and never appear independently in samples or consumer apps. This is rare and needs evidence.
- Extract shared implementation into an allowed core or technology package without changing which packages consumers must install. This is a refactor, not a merge.

The per-module empty DI and Autofac extension shells are not consolidation candidates unless a maintainer explicitly requests their removal in a tracked packaging change.

Put ergonomic aliases in documentation and samples, not in wider default dependency graphs. See the [Dependency Graph](site/content/docs/architecture/dependency-graph.md) for the living inventory.

### Feature axes

- Vertical domain packages (durable messaging, saga, and semantic mediators) must not reference each other or broker or ORM SDKs unless the dependency rule table in `site/content/docs/architecture/dependency-graph.md` explicitly allows it.
- Horizontal platform packages (runtime and transport) must not reference vertical domain abstractions.
- Put mapping between axes in feature bridges, not in platform core. This covers domain envelopes to wire format and store rows to contracts.

### Abstract package rules

Packages ending in `.Abstractions` contain only interfaces, value objects, enums, exceptions, attributes, and coordination types whose fields and parameters are abstract types. They never reference concrete implementation, storage, transport, or hosting packages.

Keep abstractions abstract on public surfaces. Parameters, return types, properties, and fields exposed by `*.Abstractions` types must be interfaces, primitives, enums, records, or other types from allowed contract roles. Do not expose concrete store, transport, ORM, broker SDK, or hosting types in public or internal API members that consumers could depend on indirectly.

### API and value object design

Public APIs group related parameters into semantic types named by role, not by parameter count. Match an existing suffix before inventing a new one. The [API Design](site/content/docs/architecture/api-design.md) page holds the concrete inventories, package maps, feature exemplars, CLR kind selection, and the inbox and outbox `Message` property rule. This section states the rules that apply to every axis.

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
| `*Item` | One atomic command with a payload and per-operation metadata | Host configuration and parallel parameter lists |
| `*Metadata` | Annotations on an `*Item`, built from grouped value objects | Module registration, mediation tuning |
| `*Request` | Operation input without a domain payload body | Full message accept or enqueue commands |
| `*Settings` | Per-invocation pipeline tuning on mediators | Stored message metadata |
| `*Options` | Module, processor, adapter, or store configuration | Per-message writer input |
| `*HostOptions` | Background-service lifecycle only | Business commands |
| `*Filter` | Query or purge predicates where optional fields are intentional | Writer commands |
| `*Binding` | Framework adapter HTTP or host binding input at the edge | Writer commands, mediation settings |
| (no suffix) | Small identifiers and cross-cutting value objects | Bags of nullable primitives |

These suffixes and shapes stay banned:

- Do not introduce `*Specification` types. In domain-driven design that suffix means a selection predicate, so use plain concept names with nested variants instead.
- Do not use `*Options` for per-message or per-command writer input.
- Do not expose parallel parameter lists that must stay aligned in length. Use `IReadOnlyList<*Item>`.
- Do not embed `CancellationToken` inside `*Item`, `*Metadata`, or `*Request`. Pass cancellation as the final method parameter only.
- Once an `*Item` or `*Request` exists, do not add writer or store overloads with two or more business parameters beyond the message body. Put optional metadata, scheduling, routing, and identity on `*Item` or `*Metadata`, not on parallel scalar parameters.

#### Metadata concern categories

When a command carries optional behavior, group it by concern inside `*Metadata` using discriminated value objects. Use explicit variants, not nullable "absent" fields:

| Concern | Typical variants |
|---------|------------------|
| Identity | generated or caller-supplied identifier |
| Idempotency | none or keyed |
| Scheduling or visibility | immediate, at absolute time, or after delay |
| Trace or correlation | none, correlated, workflow, or distributed context |
| Tenancy | unscoped or isolated tenant |
| Routing or target | default or explicit destination (axis-specific) |

Feature packages name their own value objects. Put shared cross-axis primitives in the narrowest contract role that both axes may reference. See [API Design](site/content/docs/architecture/api-design.md) for the durable-messaging application.

#### Optional data and mapping

- At the command boundary, prefer abstract record hierarchies with named variants such as `None`, `Generated`, and `Supplied` over nullable properties.
- At the persistence boundary, sparse nullable columns are allowed on envelopes and store rows.
- At the query boundary, optional predicates on `*Filter` types are allowed.
- External input such as broker or framework payloads may be nullable. Map it once at the adapter edge.

One mapper per feature owns translation from command value objects to persistence shape. Adapters translate external formats into command metadata and do not duplicate mapping logic at the store.

#### Method shape and parameter budget

- With 0 to 2 business parameters, inline parameters are acceptable when each is orthogonal.
- With 3 or more business parameters, introduce or extend an `*Item` or `*Request` before adding another parameter.
- For batch operations, accept `IReadOnlyList<*Item>`, not params arrays or parallel lists of related values.
- In async methods, put semantic input first and `CancellationToken` last.
- In mediators, pass the message with an optional `*Settings` and `CancellationToken`. This is the pattern to follow.

#### Role placement

- Put shared value objects used by multiple axes in the narrowest shared `*.Abstractions` package allowed by the role matrix.
- Put axis-specific command types such as `*Item`, `*Metadata`, and receipts in that axis's `*.Abstractions`.
- Put mediation `*Settings` in the matching semantic module abstractions package.
- Put module and host `*Options` in the package that registers the service.
- Keep internal projection helpers from command to persistence `internal` in the core implementation package for that feature.
- Adapters map and wire. They do not define alternate option types for concerns already modeled in abstractions.

#### Ergonomics

- Prefer static factories on `*Item` records, such as `OutboxEnqueueItem<T>.From(message)`, so construction stays discoverable on the type that callers pass to writer APIs.
- A writer facade may expose one body-only sugar overload per operation family. For example, it may offer `EnqueueAsync<TEvent>(TEvent message, ...)` implemented as `EnqueueAsync(OutboxEnqueueItem<TEvent>.From(message), ...)`. Do not add further overloads that take metadata scalars.
- Use `with` on `*Item` and `*Metadata` for ad-hoc composition. Add a named static helper on the `*Item` record only when a nested `with` is repetitive and has a domain name, such as `ScheduledAt` or `WithTopic`.
- Thin static helper types such as `OutboxEnqueue` and `InboxAccept` are acceptable for code that connects different shapes, such as untyped batch entries. Do not use plural `*Items` names that imply a collection type. Avoid batch wrappers that only return the array they receive.
- Keep compose-time `*Options` on module builders. Do not pass them through runtime command methods.
- When a workflow needs both mediation and durable persistence, use separate calls with separate semantic types. Do not merge the concerns.

#### Before adding a public API

1. Is this command, configuration, query, persistence, or external adapter input?
2. If command: is there a single `*Item` or `*Request`?
3. Are optional concerns expressed as variants, not nulls?
4. Are there more than two business parameters? If so, collapse to one semantic type.
5. Is `CancellationToken` on the method only?
6. Does mapping live in one place?
7. Does the suffix match the taxonomy table?
8. Does the type belong in abstractions or an adapter package per dependency role rules?

#### API alignment

When you next touch an area whose surface does not match this taxonomy, align it. This covers options objects that mix invocation tuning with cancellation or strategy references, methods with three or more scalar parameters where a request record would clarify intent, and error and lease APIs that pass parallel context fields instead of a context record. The [API Design](site/content/docs/architecture/api-design.md) page tracks specific type names, CLR kind rules (`sealed record` and `sealed class` for `*HostOptions`), `*Binding` adapter types, and target shapes. Historical mappings belong in the [Migration Guides](site/content/docs/migration/README.md).

### Composite module pattern

Modules with sub-modules implement `ICompositeModule`. `DeclareChildren` runs during `Register()` before any `Build()`. The builder action runs inside `DeclareChildren`. `Build()` registers core services only.

Express required ordering through `IRequires<TModule>` and composite ownership. Modules do not use registration markers or scan the registry during `Build()`. The registry inserts children depth-first, validates the complete dependency graph, then topologically sorts it before building any module. Registering the same module type twice throws `LiteBusConfigurationException` at compose time.

Compose through parent module builders. Register storage, dispatch, and ingress inside the parent module builder with `Use*` extensions. Do not add new top-level `IModuleRegistry` shortcuts that bypass the parent builder. Mark obsolete patterns instead of extending them.

A builder that declares children must defer its registrations. `DeclareChildren` runs before the live `IMessageRegistry` and contract registry exist, so a builder whose configuration action runs there queues type and assembly registrations and applies them in `Build()`. `CommandModuleBuilder` and `OutboxModuleBuilder` both do this. `MessageContractBuilder` is the shared deferred contract writer.

Two ordering rules follow, and breaking either one fails silently. Apply queued registrations after every source of registrations has run, including conditional enablement such as the axis selection that `AddAuditing` implies. Apply contracts before registrations. A registration queued after `ApplyRegistrations` is dropped without an error.

A feature package layered on an axis registers a child module through the axis builder. `CommandModuleBuilder.RegisterChildModule` is the seam, and `LiteBus.Commands.Recurring` is the worked example. The package is a feature bridge, not a host adapter, when it needs no host framework SDK and reaches the generic host only through the manifest. Add its role to `TryGetRole` in `ArchitectureDependencyPolicyTests` and constrain what it may reference in `IsFeatureBridgeReferenceAllowed`. The policy test fails a package with no assigned role, and that failure is the intended gate.

### Composition rules

- Applications must reference only the packages they compose. See Granular opt-in packages above. Never widen a package reference graph because another integration exists in the same repo.
- Do not add convenience APIs on shared builders that add storage, transport, or other adapters to generic DI packages.
- When an ergonomic shortcut would violate role boundaries or opt-in packaging, defer it to `site/content/docs/roadmap/README.md`.

### Adapter rules

- Ship one adapter package per integration surface: one store technology, one broker, or one host framework.
- Allow one transport module per composition. Registering a second transport module fails at composition time.
- Adapters register through `IModuleConfiguration`. They do not register `IHostedService`, `IHealthCheck`, or equivalent framework types directly.
- Observability and diagnostics use framework-neutral contracts or dedicated host-adapter `*.Extensions.*` packages.

## Runtime patterns

### Manifest and hosting

- One-shot host startup work implements `IStartupTask` with `Task RunAsync(CancellationToken cancellationToken)` and registers through `configuration.RegisterStartupTask(Type)`.
- Long-running host loops implement `IBackgroundService` with `Task ExecuteAsync(CancellationToken stoppingToken)` and register through `configuration.RegisterBackgroundService(Type)`.
- Modules register services with `configuration.DependencyRegistry.Register(DependencyDescriptor)`.
- Do not put `RegisterBackgroundService` or `RegisterStartupTask` on `IDependencyRegistry`. DI adapters must not reference `Microsoft.Extensions.Hosting`.
- The `LiteBus.Runtime.Extensions.Microsoft.Hosting` and `LiteBus.Runtime.Extensions.Autofac.Hosting` packages bridge to the generic host. `AddLiteBus` applies them after module build.
- Diagnostic probes implement `IDiagnosticCheck` and register through `configuration.RegisterDiagnosticCheck(Type, string)`. `AddLiteBus` exposes collected descriptors on `LiteBusHostManifest`.
- Register manifest entries instead of wiring the host directly. Any work the generic host must run, such as startup tasks, background loops, and diagnostic probes, registers through `IModuleConfiguration` manifest methods. Core and adapter packages must not register host framework types directly.

See `site/content/docs/architecture/hosted-services.md` for registration examples and feature-specific hosted types.

### Contract registration

- Module builders use `IContractWriter` at configuration time.
- Dispatchers and envelope factories use `IContractReader` at runtime.
- `IMessageContractRegistry` extends both and is the DI singleton key.
- `MessageContractBuilder` defers registrations until the parent module `Build()`.
- Runtime code reads `[MessageContract]` through `AddFromAssembly` and on demand in `GetContract`.

### Message registry

- Module builders use `IMessageWriter` at configuration time through `Register(Type)`.
- The mediator and resolve strategies use `IMessageReader` at runtime through `Find`, enumeration, `Handlers`, and `Count`.
- `IMessageRegistry` extends both and is the DI singleton key. Each `IModuleConfiguration` gets one instance, created in `MessageModule.Build()` through `GetOrCreateContext`.
- Do not use a process-wide static accessor or `Clear()`. Tests use a new `MessageRegistry` instance per case.

## Public contract stability

- Telemetry meter names, activity source names, and instrument name constants on public telemetry types are part of the consumer contract. Treat renames and removals as breaking changes.
- When adding instruments, define names as public `const string` on the telemetry type, register meters through the matching host-adapter `*.Extensions.OpenTelemetry` package, and document new names in `site/content/docs/architecture/README.md`.
- Builder method renames, manifest entry changes, and persisted envelope field semantics are breaking changes. Update the docs in the same change.
- Prefer stable contract names and versions over assembly-qualified CLR names in persisted envelopes.

## Package and framework dependencies

Keep each package's dependency graph minimal and aligned with its dependency role. Before adding a NuGet or project reference, confirm that the consuming code uses a type or member that requires it.

- Prefer the BCL and abstractions already in the graph. `IServiceProvider.GetService(Type)` is in `System`, so do not add `Microsoft.Extensions.DependencyInjection*` packages only to call `GetService<T>()` or other extension methods. Use the non-generic API or inject the required service through constructors and module registration instead.
- Restrict hosting and framework packages to host adapters. Other roles must not reference `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Diagnostics.HealthChecks`, ASP.NET Core, or other host frameworks.
- Keep one integration surface per concern. Put observability, health, and diagnostics in framework-neutral contracts such as `IDiagnosticCheck` and OpenTelemetry meters on public constants, or in dedicated `*.Extensions.*` adapter packages.
- Justify every new reference in review. If a feature can be expressed with an existing abstraction, manifest entry, or documented application code, prefer that over a new package dependency.

## New package checklist

Answer these questions before adding a project:

1. Which dependency role and role suffix does it need?
2. Can an existing package absorb this without a forbidden role edge?
3. Does it need a new `*.Abstractions` package or fit an existing one?
4. Does it need manifest registration as a startup task, background service, or diagnostic check?
5. Does `site/content/docs/architecture/dependency-graph.md` need a new row?
6. Does `site/content/docs/architecture/README.md` need a feature section or invariant note?

## Analyzers

- Ship compile-time rules in `LiteBus.Analyzers` only. Take no runtime dependency on mediator or durable packages.
- Keep the rule inventory in `site/content/docs/reference/analyzers.md` aligned with `DiagnosticIds` (LB1001-LB1024). LB1002 and LB1006 are reserved and never shipped.
- LB1007 covers handled durable types that miss contract registration. Honor `RegisterFromAssembly` the same as explicit `Contracts.Register`.
- LB1017 covers attributed durable types. Match only `IContractWriter` or `IMessageContractRegistry` `Register` invocations, not unrelated `Register<T>()` methods.
- LB1004 must cover `AcceptAsync`, `AcceptBatchAsync`, and the `ITransactionalInbox` acceptance APIs.
- When changing diagnostic behavior, add or update analyzer tests in `tests/LiteBus.Analyzers.Tests`.

## Build and repo hygiene

- Run `dotnet build LiteBus.slnx` after editing `src/`.
- Strong-named assemblies use `LiteBus.snk`, which is committed, and every build signs with it. Strong naming is assembly identity, not a security boundary. `InternalsVisibleTo` must include the solution public key, which is `$(LiteBusPublicKey)` in `src/Directory.Build.props`. Never change the key, because it is part of every published assembly identity.
- Keep package versions central. Add NuGet packages to `src/Directory.Packages.props`, not inline in csproj files.
- In a new test project that uses xUnit attributes, add `GlobalUsings.cs` with `global using Xunit;` when the project has no other global usings for xUnit.

## Releases and changelog

- A version has one Changelog section, and every preview after the first has its own. `## v7.0.0` accumulates everything landing in 7.0.0, and it is what the first preview and the stable release publish, so each of those describes the whole line. Every later preview adds a `## v7.0.0-preview.N` section above it that holds only what changed since `preview.N-1`. The release workflow publishes that section instead. A reader moving from one preview to the next wants the difference, not the two hundred lines they already read when they took the previous preview. A reader arriving at the line for the first time wants all of it, which is what the first preview and the stable release give them.
- A delta section names what it compares against and links to the cumulative one. Open it with a line that says which preview it is a delta against and that points at the version's own section, so the whole line stays one link away from anyone who wants it. `.github/scripts/generate-release-notes.sh` also puts that link in the pre-release banner it prepends.
- Do not add a section for the first preview of a line. `.github/workflows/release.yml` prefers a section matching the tag and falls back to the version's own, so a `## v7.0.0-preview.1` section would turn the first preview into a delta against nothing.
- Write a delta section when the preview is cut. Do not derive it from git. Entries land in the version's cumulative section as the work happens. Cutting a preview after the first means summarizing what accumulated since the previous tag into a new delta section, in the same commit as the version bump. Keep it to about a line per item and leave the reasoning in the cumulative section, so the two cannot disagree about the same change.
- `Changelog.md` holds the current major line only. When a new major version ships, move the previous major's sections to the top of `Changelog.Archive.md`, below its intro, and keep the `## Earlier Releases` pointer as the last section of `Changelog.md`. The release workflow reads one section from `Changelog.md`, and the site publishes that file as `/docs/changelog`, so the archive only needs to stay linked.
- CI proves a commit once, and the tag publishes it. `ci.yml` runs every gate on each push to `main` and to version branches. Pushing a `v*` tag runs `release.yml`, which reruns no tests. It checks the tag and changelog, requires a successful CI push run on the tagged commit, and waits up to 45 minutes for a run that is still running. It then builds, packs, and publishes to nuget.org through trusted publishing, to GitHub Packages, and as a GitHub release. Push the branch before the tag. Do not run `gh release create` by hand for a pushed tag, because it races the workflow or makes the workflow fail.

## Testing

- Run unit tests locally and leave integration suites to CI. Local verification runs the `tests/*.UnitTests` projects with `dotnet test tests/<Project>.UnitTests --no-build`. Do not run `dotnet test LiteBus.slnx` or the `*.IntegrationTests` projects locally, because they start PostgreSQL, SQL Server, MySQL, and broker containers through Testcontainers and take far longer on one machine than in CI. `ci.yml` shards them across runners on every push, and the project is public, so CI minutes are not a constraint. Push the branch and read the CI result instead. Run one integration class locally only when debugging a failure that CI reported.
- Tests prove behavior, not wiring. Prefer assertions on outcomes such as manifest contents, probe results, processor pass behavior, and store state transitions over tests that only verify that a type appears in dependency injection or that a module builds without exception.
- Use a fresh `MessageRegistry` or an isolated module configuration per test case. Do not rely on process-wide static state.
- When fixing manifest ordering, diagnostic registration, telemetry recording, or composite module child expansion, add regression tests.

## Documentation

- Docs move with API changes. When adding, renaming, or removing public builder methods, contracts, manifest entries, metric names, or registration patterns, update the matching section in `site/content/docs/` in the same change. This is usually `Architecture.md`, `API-Design.md`, `Hosted-services.md`, or a feature guide, and `Roadmap.md` when scope shifts.
- Document application-owned integration such as health endpoints, schema probes, and export sinks as recipes. Ship framework-neutral contracts and stable telemetry names in libraries.
- Keep `site/content/docs/architecture/hosted-services.md` and `site/content/docs/architecture/README.md` aligned with the manifest model: `IStartupTask`, `IBackgroundService`, `IDiagnosticCheck`, and `LiteBusHostManifest`.
- Keep `site/content/docs/architecture/dependency-graph.md` as the living package inventory and dependency rule reference.
- The documentation is also served as plain text for agents. `/llms.txt`, `/llms-full.txt`, and a `.md` endpoint per page are generated from the same Markdown and the same page tree that the navigation uses, so a page added to a `meta.json` reaches them with no separate edit. Titles and descriptions are derived from each page's prose, so a page that opens with neither an intro paragraph, a `Summary` metadata field, nor a `## Summary` section reaches the index with no note and fails `site/scripts/check-llms.mjs`. The hand-written parts, the summary and the `Start Here` list, are in `site/lib/llms.ts`. `site/README.md` documents the endpoints.
- The site documents the branch it is built from. There are no versioned documentation snapshots. `site/content/docs/` is the one documentation set, served at `/docs`. Cutting a release updates `latestRelease` in `site/lib/site.ts`. Old `/v7/` documentation links redirect to the same path without the prefix.

## Evolving this guide

These instructions and `site/content/docs/` should reflect how the codebase is built. Agents and maintainers may change any rule when the project direction shifts. Propose amendments in the same PR or conversation as the code change that motivates them, so guidance and implementation stay aligned.
