# Analyzers

`LiteBus.Analyzers` ships Roslyn rules that catch handler registration mistakes, durable contract gaps, and inbox misuse at compile time. Reference the package from application and test projects that use LiteBus mediators or durable writers.

```xml
<PackageReference Include="LiteBus.Analyzers" PrivateAssets="all" />
```

Analyzers have no runtime dependency on LiteBus libraries.

## Rule Inventory (LB1001-LB1018)

| ID | Severity | Category | Summary |
| --- | --- | --- | --- |
| LB1001 | Error | Handlers | Duplicate `ICommandHandler<TCommand>` for the same command type |
| LB1002 | Reserved | Handlers | Not implemented. Event multicast is supported, so duplicate event handlers are not reported by this rule. |
| LB1003 | Warning | Handlers | Query or stream query handler depends on command, event, inbox, or outbox APIs |
| LB1004 | Error | Inbox | `ICommand<TResult>` passed to `IInbox.AcceptAsync`, `AcceptBatchAsync`, or `ITransactionalInbox.AcceptAsync` |
| LB1005 | Error | Handlers | Open generic handler exposes unsupported generic arity |
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
| LB1018 | Warning (disabled by default) | Auditing | Command or query type declares neither `[Audited]` nor `[AuditExempt]` and has no `IAuditDefinition` facet |

### Audit Declaration (LB1018)

LB1018 makes the selection of audited events total: every command and query states a position, and the reason for not auditing is recorded beside the message rather than in a document that drifts. It is **disabled by default**, because enabling it silently would break every existing compilation, and it stays silent when the audit contracts are not referenced at all.

Enable it once the codebase has declared its position:

```ini
# .editorconfig
[*.cs]
dotnet_diagnostic.LB1018.severity = warning
```

Promote it to `error` when the trail is a compliance obligation rather than a convenience. See [Auditing](../concepts/auditing.md).

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
