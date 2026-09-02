# Analyzers

`LiteBus.Analyzers` ships Roslyn rules that catch handler registration mistakes, durable contract gaps, and inbox misuse at compile time. Reference the package from application and test projects that use LiteBus mediators or durable writers.

```xml
<PackageReference Include="LiteBus.Analyzers" PrivateAssets="all" />
```

Analyzers have no runtime dependency on LiteBus libraries.

## Rule Inventory (LB1001-LB1021)

| ID | Severity | Category | Summary |
| --- | --- | --- | --- |
| LB1001 | Error | Handlers | Duplicate `ICommandHandler<TCommand>` for the same command type |
| LB1002 | Reserved | Handlers | Not implemented. Event multicast is supported, so duplicate event handlers are not reported by this rule. |
| LB1003 | Warning | Handlers | Query or stream query handler depends on command, event, inbox, or outbox APIs |
| LB1004 | Error | Inbox | `ICommand<TResult>` passed to `IInbox.AcceptAsync`, `AcceptBatchAsync`, or `ITransactionalInbox.AcceptAsync` |
| LB1005 | Error | Handlers | Open generic handler exposes unsupported generic arity: anything but one type parameter, or two bound by a handler contract taking both in order |
| LB1007 | Warning | Contracts | Handled durable command/event lacks `[MessageContract]` and explicit `Contracts.Register` / `RegisterFromAssembly` |
| LB1008 | Error | Handlers | Command type has no main command handler in the compilation |
| LB1009 | Error | Handlers | Query or stream query type has no main handler in the compilation |
| LB1010 | Error | Handlers | Duplicate `IQueryHandler<TQuery, TResult>` or `IStreamQueryHandler<TQuery, TResult>` for the same query type |
| LB1011 | Warning | Handlers | `[HandlerTag]` is not referenced by command, query, or event mediation tag filters |
| LB1012 | Warning | Handlers | Same handler simple name appears in multiple assemblies (risk of double `RegisterFromAssembly`) |
| LB1013 | Warning | Outbox | Constructor injects `ITransactionalOutboxStore` without a `DbContext` in the same constructor |
| LB1014 | Error | Configuration | Inbox or outbox processor enabled without a dispatcher in the same module builder scope |
| LB1015 | Warning | Configuration | Transactional EF storage calls `EnforceTransactionalSetup()` without `EnableSaveChangesInterceptor()` |
| LB1016 | Warning | Inbox | Constructor injects `ITransactionalInboxStore` without a `DbContext` in the same constructor |
| LB1017 | Warning | Contracts | Type declares `[MessageContract]` but lacks explicit `Contracts.Register` or `RegisterFromAssembly` in the compilation |
| LB1018 | Warning (disabled by default) | Auditing | Command or query type declares neither `[Audited]` nor `[AuditExempt]` and has no `IAuditDefinition`. The preconfigured instance of LB1020 over `AuditDeclaration` |
| LB1019 | Warning | Handlers | Shortcut implements `IMessageShortcut<TMessage>` for a message that produces a result |
| LB1020 | Warning (disabled by default) | Declarations | Command or query type states no position on a metadata value type named in `litebus_required_declarations` |
| LB1021 | Warning | Declarations | `litebus_required_declarations` names a type that does not resolve in the compilation |

### Audit Declaration (LB1018)

LB1018 makes the selection of audited events total: every command and query states a position, and the reason for not auditing is recorded beside the message rather than in a document that drifts. It is **disabled by default**, because enabling it silently would break every existing compilation, and it stays silent when the audit contracts are not referenced at all.

Enable it once the codebase has declared its position:

```ini
# .editorconfig
[*.cs]
dotnet_diagnostic.LB1018.severity = warning
```

Promote it to `error` when the trail is a compliance obligation rather than a convenience. See [Auditing](../concepts/auditing.md).

### Required Declarations (LB1020, LB1021)

LB1020 is the general form of LB1018. Name the metadata value types your project requires every message to state a position on, and enable the rule:

```ini
# .editorconfig
[*.cs]
litebus_required_declarations = App.Security.RequiredPermission, App.Compliance.RetentionClass
dotnet_diagnostic.LB1020.severity = warning
```

A message satisfies it with a definition class, with an attribute annotated `[MessageDeclaration(typeof(TValue))]`, or with `[DeclarationExempt(typeof(TValue), "rationale")]`. A declaration written for a base type or marker interface covers the messages beneath it. With nothing configured the rule reports nothing.

LB1021 reports a configured name that does not resolve, because a name that silently did nothing would disable the requirement it configures, and a typo in `.editorconfig` is exactly how that happens.

`RequireDeclaration<TValue>()` on the messaging module enforces the same rule at composition time, which also covers a message registered from an assembly this compilation never saw. See [Required Declarations](../catalog/analyzers/required-declarations.md).

### Untyped Shortcut on a Result Message (LB1019)

`ICommand<TResult>` derives from `ICommand`, so `ICommandShortcut<CreateProductCommand>` compiles for a command that produces a result. The untyped `Shortcut` carries no result, so answering from such a shortcut reaches the caller as `LiteBusConfigurationException` instead of the value the caller expects.

For a message that produces a result the typed contract is a strict superset, so LB1019 reports the declaration rather than the individual call: the contract choice is the mistake. Guards are never reported, because a refusal does not owe the caller a result and the untyped guard is correct everywhere.

```csharp
// LB1019: CreateProductCommand produces Guid
public sealed class SkipCreatedProduct : ICommandShortcut<CreateProductCommand> { }

// Correct
public sealed class SkipCreatedProduct : ICommandShortcut<CreateProductCommand, Guid> { }
```

Open generic shortcuts are not reported: the message type is a type parameter, so the result type is unknown until dispatch. See [The Handler Pipeline](../concepts/handler-pipeline.md).

### Contract Registration Split (LB1007 vs LB1017)

- **LB1007** fires on **handled** durable types that have neither `[MessageContract]` nor registration discovered through `Contracts.Register<T>()` or `RegisterFromAssembly`.
- **LB1017** fires on **attributed** durable types that lack explicit registration. Runtime on-demand resolution still works; explicit registration is recommended for predictable discovery.

Both rules treat `RegisterFromAssembly` / `AddFromAssembly` as satisfying explicit registration. LB1017 matches only `IContractWriter` / `IMessageContractRegistry` `Register` invocations, not unrelated `Register<T>()` methods.

### Inbox Result Commands (LB1004)

The inbox replays stored commands later and discards handler results. Only result-less `ICommand` types may be accepted. LB1004 covers `AcceptAsync`, `AcceptBatchAsync` (including `InboxAcceptItem.From` in array and collection-expression batches), and transactional inbox acceptance APIs.

### Query and Stream Query Handlers (LB1009 and LB1010)

LB1009 reports query types without `IQueryHandler<TQuery, TResult>` and stream query types without `IStreamQueryHandler<TQuery, TResult>`. Stream queries implementing `IStreamQuery<TResult>` are not checked against query handlers. LB1010 applies the same duplicate detection to both handler kinds. Message type discovery for LB1008, LB1009, and LB1010, and handler registration scanning, include eligible referenced assemblies (same filter as handler registration scanning).

### Reserved Event Handler Slot (LB1002)

LB1002 is intentionally unused in the shipped analyzer set. Event mediation supports multiple handlers for one event type, so the command and query duplicate rules do not have an event equivalent. LB1012 still warns when the same handler simple name appears in multiple assemblies.

## Suppression

Use `#pragma warning disable LB1007` (or the relevant ID) sparingly. Prefer fixing registration or handler shape.

## See Also

- [Troubleshooting](../operations/troubleshooting.md): common diagnostic messages
- [Inbox](../reliable-messaging/inbox.md): durable accept API and contract registration
- [Dependency graph](../architecture/dependency-graph.md): package that ships analyzers
