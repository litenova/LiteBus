# Analyzers

`LiteBus.Analyzers` ships Roslyn rules that catch handler registration mistakes, durable contract gaps, and inbox misuse at compile time. Reference the package from application and test projects that use LiteBus mediators or durable writers.

> **This page is for** developers who write handlers and durable contracts and want registration mistakes caught at compile time.
>
> **After reading it** you can look up any rule ID the build reports and fix what it flags. For runtime failures, continue with [Troubleshooting](../operations/troubleshooting.md).

```xml
<PackageReference Include="LiteBus.Analyzers" PrivateAssets="all" />
```

Analyzers have no runtime dependency on LiteBus libraries.

## Rule Inventory (LB1001-LB1024)

| ID | Severity | Category | Summary |
| --- | --- | --- | --- |
| LB1001 | Error | Handlers | Duplicate `ICommandHandler<TCommand>` for the same command type |
| LB1002 | Reserved | Handlers | Reserved, never shipped. Event multicast is supported, so duplicate event handlers are not reported. |
| LB1003 | Warning | Handlers | Query or stream query handler depends on command, event, query, inbox, outbox, or transport publisher APIs |
| LB1004 | Error | Inbox | `ICommand<TResult>` passed to `IInbox.AcceptAsync`, `AcceptBatchAsync`, or `ITransactionalInbox.AcceptAsync` |
| LB1005 | Error | Handlers | Open generic handler has a type parameter count other than one, or other than two when the handler contract takes both in order |
| LB1006 | Reserved | N/A | Reserved, never shipped. |
| LB1007 | Warning | Contracts | Handled durable command or event lacks `[MessageContract]` and explicit `Contracts.Register` / `RegisterFromAssembly` |
| LB1008 | Error | Handlers | Command type has no main command handler in the compilation |
| LB1009 | Error | Handlers | Query or stream query type has no main handler in the compilation |
| LB1010 | Error | Handlers | Duplicate `IQueryHandler<TQuery, TResult>` or `IStreamQueryHandler<TQuery, TResult>` for the same query type |
| LB1011 | Warning | Handlers | No command, query, or event mediation tag filter references `[HandlerTag]` |
| LB1012 | Warning | Handlers | Same handler simple name appears in multiple assemblies, which risks a double `RegisterFromAssembly` |
| LB1013 | Warning | Outbox | Constructor injects `ITransactionalOutboxStore` without a `DbContext` in the same constructor |
| LB1014 | Error | Configuration | Inbox or outbox processor enabled without a dispatcher in the same module builder scope |
| LB1015 | Warning | Configuration | Transactional EF storage calls `EnforceTransactionalSetup()` without `EnableSaveChangesInterceptor()` |
| LB1016 | Warning | Inbox | Constructor injects `ITransactionalInboxStore` without a `DbContext` in the same constructor |
| LB1017 | Warning | Contracts | Type declares `[MessageContract]` but lacks explicit `Contracts.Register` or `RegisterFromAssembly` in the compilation |
| LB1018 | Warning (disabled by default) | Auditing | Command or query type declares neither `[Audited]` nor `[AuditExempt]` and has no `IAuditDefinition`. This rule is LB1020 with `AuditDeclaration` already configured |
| LB1019 | Warning | Handlers | Shortcut implements `IMessageShortcut<TMessage>` for a message that produces a result |
| LB1020 | Warning (disabled by default) | Declarations | Command or query type states no position on a metadata value type named in `litebus_required_declarations` |
| LB1021 | Warning | Declarations | `litebus_required_declarations` names a type that does not resolve in the compilation |
| LB1022 | Warning | Handlers | `[HandlerPriority]` value falls inside the range LiteBus reserves for its own handlers |
| LB1023 | Warning (disabled by default) | Handlers | Command handler sends or publishes another message through a mediator inside its handle method |
| LB1024 | Warning (disabled by default) | Handlers | Command handler injects an external side-effect type through its constructor |

## Build-Only Rules

LB1001, LB1007, LB1008, LB1009, LB1010, LB1011, LB1012, LB1017, LB1018, LB1020, and LB1021 need facts from the whole compilation. A handler in one file satisfies a command declared in another, and a `Register` call in a module satisfies a contract declared elsewhere. These rules gather facts as each file is analyzed and report once at compilation end. Their descriptors carry the `CompilationEnd` tag.

`dotnet build` and CI report them as usual. Visual Studio and other hosts that analyze one open file at a time do not show them inline while you edit. They appear in the error list after a build, or with full solution analysis enabled. The remaining rules inspect one type or one invocation and report live while you type.

## Compilation Boundaries

Compilation-end rules analyze one project at a time. They see the syntax of the compilation under analysis plus the types declared in eligible referenced assemblies. Registrations, tag filters, and handlers that live in another project are invisible, so the rule still reports a message handled or registered in an upper layer when it analyzes the lower layer.

Message candidates for LB1007, LB1008, LB1009, LB1011, and LB1017 are scoped to the analyzed assembly, because a message from a referenced assembly cannot be annotated from the compilation being built. Handlers from eligible referenced assemblies still satisfy LB1008 and LB1009 coverage. Keep the handler or the registration call in the same compilation as the message type, or suppress the diagnostic at the declaration with a comment naming the project that owns it. LB1012 is the exception. It compares handler simple names across assemblies by design.

## Reserved Handler Priority (LB1022)

`HandlerPriorities` reserves the window from `ReservedFloor` (1,000,000) up to but not including `ReservedCeiling` (2,000,000) for the handlers LiteBus ships, such as the persistence writer and the audit writer. Your application owns everything below the floor and everything at or above the ceiling. The rule applies to handler types only. A class that implements no handler interface is skipped even when it carries the attribute, because the attribute has no effect there.

Using a stage constant to order two application handlers against each other compiles and runs, but it orders each handler against the library stages rather than against its sibling.

```csharp
// Wrong: ordered against LiteBus's own commit, not against the other handler.
[HandlerPriority(HandlerPriorities.Persistence)]
public sealed class AdvancePriceTiers : IEventHandler<OrderConfirmedEvent>;
```

Order application handlers against each other with plain values below the floor. Use the band at or above `ReservedCeiling` for infrastructure that has to run after every LiteBus handler.

```csharp
[HandlerPriority(10)]
public sealed class IssueTickets : IEventHandler<OrderConfirmedEvent>;

[HandlerPriority(20)]
public sealed class AdvancePriceTiers : IEventHandler<OrderConfirmedEvent>;

// Runs after every LiteBus handler, so an audit record written at Observability is inside the transaction.
[HandlerPriority(HandlerPriorities.UnitOfWork)]
public sealed class CommitUnitOfWork : IEventCompletionHandler<OrderConfirmedEvent>;
```

Only `Persistence` and `Observability` may be reordered between LiteBus releases, and only relative to each other. The floor and the ceiling are stable, so the two application bands they delimit are stable too.

## Audit Declaration (LB1018)

LB1018 requires every command and query to state an audit position, so the reason for not auditing sits beside the message instead of in a separate document that can fall out of date. The rule is disabled by default, because enabling it silently would break every existing compilation. It also reports nothing when the audit contracts are not referenced at all.

Enable it once your codebase has declared its position.

```ini
# .editorconfig
[*.cs]
dotnet_diagnostic.LB1018.severity = warning
```

Promote it to `error` when the trail is a compliance obligation rather than a convenience. See [Auditing](../concepts/auditing.md).

## Required Declarations (LB1020, LB1021)

LB1020 is the general form of LB1018. Name the metadata value types your project requires every message to state a position on, then enable the rule.

```ini
# .editorconfig
[*.cs]
litebus_required_declarations = App.Security.RequiredPermission, App.Compliance.RetentionClass
dotnet_diagnostic.LB1020.severity = warning
```

A message satisfies the rule with a definition class, with an attribute annotated `[MessageDeclaration(typeof(TValue))]`, or with `[DeclarationExempt(typeof(TValue), "rationale")]`. A declaration written for a base type or marker interface covers the messages that derive from it. With nothing configured the rule reports nothing.

LB1021 reports a configured name that does not resolve, because a name that silently did nothing would disable the requirement it configures. A typo in `.editorconfig` is the usual cause.

`RequireDeclaration<TValue>()` on the messaging module enforces the same rule at composition time, which also covers a message registered from an assembly outside this compilation. See [Required Declarations](../catalog/analyzers/required-declarations.md).

## Nested Mediation in Command Handlers (LB1023)

LB1023 reports a command handler that sends a command, publishes an event, or mediates another message through `ICommandMediator`, `IEventMediator`, or `IMessageMediator` inside `HandleAsync` or `Handle`. The nested mediation runs a second unit of work inside the first, so the outer commit cannot roll the inner work back. A retry of the outer handler runs the inner work twice. The rule is disabled by default. Enable it once command handlers no longer nest units of work.

```ini
# .editorconfig
[*.cs]
dotnet_diagnostic.LB1023.severity = warning
```

Reads through `IQueryMediator` are not reported. Queries are side-effect free by contract (see LB1003), so a nested read creates no nested unit of work. `EvaluateAsync` guard checks are not reported either, because they decide without acting. A `Mediate` call whose message is a query is treated as a read for the same reason.

When the handler must cause follow-up work, stage it instead of mediating it inline. Collect domain events and drain them into the transactional outbox after the handler succeeds. See [Nested Mediation in Command Handler](../catalog/analyzers/nested-mediation-in-command-handler.md).

## External Side Effects in Command Handlers (LB1024)

LB1024 reports a command handler that injects an external side-effect type through its constructor. The default list is `System.Net.Http.HttpClient` and `System.Net.Http.IHttpClientFactory`. An HTTP call made before the commit leaves an effect the unit of work cannot roll back. The rule is disabled by default. Enable it and, when the defaults do not fit, name the project's own clients for external systems.

```ini
# .editorconfig
[*.cs]
litebus.LB1024.forbidden_types = App.Billing.BillingClient, App.Devices.DeviceGateway
dotnet_diagnostic.LB1024.severity = warning
```

The configured list replaces the defaults, so a configured value that names nothing reports nothing. Names that do not resolve are ignored. Only public constructor parameters are reported. A side-effect type that appears only as a method parameter, a property, or a private constructor parameter is not reported. Move the call to run after the commit, through the transactional outbox or in a completion handler. See [External Side Effect in Command Handler](../catalog/analyzers/external-side-effect-in-command-handler.md).

## Untyped Shortcut on a Result Message (LB1019)

`ICommand<TResult>` derives from `ICommand`, so `ICommandShortcut<CreateProductCommand>` compiles for a command that produces a result. The untyped `Shortcut` carries no result, so when such a shortcut answers, the caller gets a `LiteBusConfigurationException` instead of the value it expects.

For a message that produces a result the typed contract is a strict superset, so LB1019 reports the declaration rather than the individual call. The contract choice is the mistake. Guards are never reported, because a refusal carries no result for the caller and the untyped guard is correct everywhere.

```csharp
// LB1019: CreateProductCommand produces Guid
public sealed class SkipCreatedProduct : ICommandShortcut<CreateProductCommand> { }

// Correct
public sealed class SkipCreatedProduct : ICommandShortcut<CreateProductCommand, Guid> { }
```

Open generic shortcuts are not reported. The message type is a type parameter, so the result type is unknown until dispatch. See [The Handler Pipeline](../concepts/handler-pipeline.md).

## Contract Registration Split (LB1007 vs LB1017)

- **LB1007** reports handled durable types that have neither `[MessageContract]` nor registration discovered through `Contracts.Register<T>()` or `RegisterFromAssembly`.
- **LB1017** reports attributed durable types that lack explicit registration. Runtime on-demand resolution still works, but explicit registration is recommended for predictable discovery.

Both rules treat `RegisterFromAssembly` / `AddFromAssembly` as satisfying explicit registration, except scans through the command, event, and query axis builders, which record message and handler types only. Both rules match a singular `Register` only when it is called on a contract writer surface, namely `IContractWriter`, `IMessageContractRegistry`, `MessageContractBuilder`, their implementers, or `Register` extensions on those. They ignore unrelated `Register<T>()` methods such as the message, command, event, and query axis builders or a same-shaped handler registry.

Assembly scans satisfy the rules when the argument is `typeof(T)`, `typeof(T).Assembly`, a local initialized to either form, or `Assembly.GetExecutingAssembly()` / `Assembly.GetCallingAssembly()`. Any other shape, such as an `Assembly` parameter, property, or unresolved call result, does not satisfy the rules. Hoist the value into a `typeof`-based local at the call site.

## Inbox Result Commands (LB1004)

The inbox replays stored commands later and discards handler results. Only `ICommand` types without a result may be accepted. LB1004 covers `AcceptAsync`, `AcceptBatchAsync` (including `InboxAcceptItem.From` in array and collection-expression batches), and transactional inbox acceptance APIs. The rule resolves untyped items through `MessageType = typeof(X)` when it is present, and through the `Message` assignment otherwise, mirroring the runtime contract lookup. Arguments match parameters by name, so reordered named arguments resolve correctly. Batches that are not inline at the call site, such as a method result, a parameter, or a field, cannot be analyzed statically, so the rule reports nothing for them, because their element type is always `InboxAcceptItem`.

## Query and Stream Query Handlers (LB1009 and LB1010)

LB1009 reports query types without `IQueryHandler<TQuery, TResult>` and stream query types without `IStreamQueryHandler<TQuery, TResult>`. The rule does not check stream queries implementing `IStreamQuery<TResult>` against query handlers. LB1010 applies the same duplicate detection to both handler kinds.

Message candidates for LB1008 and LB1009 are scoped to the analyzed assembly. A message from a referenced assembly is never reported, because it cannot be annotated from the compilation being built. Handler registration scanning still includes eligible referenced assemblies, so a handler in a referenced assembly satisfies coverage. LB1010 duplicate detection spans the same registrations. See Compilation Boundaries.

## Reserved Event Handler Slot (LB1002)

LB1002 is intentionally unused in the shipped analyzer set. Event mediation supports multiple handlers for one event type, so the command and query duplicate rules do not have an event equivalent. LB1012 still warns when the same handler simple name appears in multiple assemblies.

## Suppression

Use `#pragma warning disable LB1007` (or the ID of the rule that fired) sparingly. Prefer fixing the registration or the handler shape.

## Next

- [Troubleshooting](../operations/troubleshooting.md): common diagnostic messages
- [Inbox](../reliable-messaging/inbox.md): durable accept API and contract registration
- [Dependency graph](../architecture/dependency-graph.md): package that ships analyzers
