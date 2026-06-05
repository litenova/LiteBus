# LiteBus agent instructions

## XML documentation (required)

All C# under `src/` must use XML documentation comments (`///`) on every construct, including `private` and `internal` members. This applies to the shipping libraries consumers reference and to internal implementation details agents maintain.

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
/// <param name="name">The stable contract name stored in inbox and outbox envelopes.</param>
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

## Background services and hosting

- One-shot host startup work implements `IStartupTask` in `LiteBus.Runtime.Abstractions` with `Task RunAsync(CancellationToken cancellationToken)` and registers through `configuration.RegisterStartupTask(Type)`.
- Long-running host loops implement `IBackgroundService` with `Task ExecuteAsync(CancellationToken stoppingToken)` and register through `configuration.RegisterBackgroundService(Type)`.
- Modules register services with `configuration.DependencyRegistry.Register(DependencyDescriptor)`.
- Do not put `RegisterBackgroundService` or `RegisterStartupTask` on `IDependencyRegistry`. DI adapters must not reference `Microsoft.Extensions.Hosting`.
- Generic host bridging lives in `LiteBus.Runtime.Extensions.Microsoft.Hosting` and `LiteBus.Runtime.Extensions.Autofac.Hosting` (`StartupTaskPhaseHostedService`, `BackgroundServiceHostAdapter`, applied from `AddLiteBus` after module build).
- Feature types: `InboxProcessorBackgroundService`, `AmqpInboxConsumer`, `PostgreSqlInboxSchemaInitializer` (`IStartupTask`). Builder APIs: `EnableInboxProcessor`, `EnableOutboxProcessor`, `DisableSchemaInitialization`, `DisableIngressConsumer`.

See `docs/Hosted-services.md` for registration examples.

## Architecture and governing principles

### Layer model

Every package belongs to exactly one layer. A package may only reference packages in the same layer or layers strictly below it.

| Layer | Number | Contents |
|---|---|---|
| Platform Contracts | 0 | Runtime.Abstractions |
| Domain Abstractions | 1 | Messaging.Abstractions, Commands.Abstractions, Events.Abstractions, Queries.Abstractions, Inbox.Abstractions, Outbox.Abstractions |
| Core Implementations | 2 | Runtime, Messaging, Commands, Events, Queries, Inbox, Outbox, Transport.Amqp |
| Shared Storage Infrastructure | 3 | Storage.PostgreSql, Storage.EfCore |
| Integration Adapters | 4 | Inbox.Storage.*, Inbox.Dispatch.*, Inbox.Ingress.*, Outbox.Storage.*, Outbox.Dispatch.* |
| Hosting / Composition | 5 | Runtime.Extensions.Microsoft.DI, Runtime.Extensions.Autofac |

### Abstract package rules

Packages ending in `.Abstractions` contain only interfaces, value objects, enums, exceptions, attributes, and coordination types whose fields and parameters are abstract types. They never reference concrete implementation, storage, transport, or hosting packages.

### Composite module pattern

Modules with sub-modules implement `ICompositeModule`. `DeclareChildren` runs during `Register()` before any `Build()`. The builder action runs inside `DeclareChildren`. `Build()` registers core services only. Sub-modules check for a parent context marker as their first `Build()` operation. The registry inserts children depth-first after the parent, then topological sort runs. Duplicate registration of the same module type is a silent no-op.

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

### Correctness invariants

- `IsAvailable` must guard `LeaseExpiresAt is not null` when status is processing/publishing.
- Inbox and outbox store implementations register one singleton instance for writer, lease, and state interfaces.
- `trace_context` must be wired end-to-end when present in schema (envelopes, mappings, SQL, lease rows).