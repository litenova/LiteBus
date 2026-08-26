# Analyzers Axis Capability Catalog

`LiteBus.Analyzers` adds compile-time rules for handler coverage, handler uniqueness, durable contract registration, inbox acceptance shape, and transactional configuration wiring. The package has no runtime dependency and can be referenced from application projects and test projects.

```xml
<PackageReference Include="LiteBus.Analyzers" PrivateAssets="all" />
```

## Capability Index

| ID | Name | Maturity |
| --- | --- | --- |
| [analyzers.missing-command-handler](handler-coverage.md) | Missing command handler | GA |
| [analyzers.missing-query-handler](handler-coverage.md) | Missing query or stream query handler | GA |
| [analyzers.duplicate-command-handler](handler-duplicates.md) | Duplicate command handler | GA |
| [analyzers.duplicate-query-handler](handler-duplicates.md) | Duplicate query or stream query handler | GA |
| [analyzers.cross-assembly-handler-name](cross-assembly-handlers.md) | Cross-assembly handler name collision | GA |
| [analyzers.missing-contract-on-handled-type](contract-registration.md) | Handled durable type missing contract registration | GA |
| [analyzers.explicit-contract-registration](contract-registration.md) | Attributed durable type missing explicit registration | GA |
| [analyzers.inbox-result-command-guard](inbox-accept-rules.md) | Result command passed to inbox accept APIs | GA |
| [analyzers.orphan-handler-tag](handler-tags.md) | Handler tag not referenced by mediation routing | GA |
| [analyzers.query-handler-purity](query-handler-purity.md) | Query handler depends on side-effecting API | GA |
| [analyzers.open-generic-handler-shape](open-generic-handlers.md) | Unsupported open generic handler shape | GA |
| [analyzers.processor-dispatcher-coupling](processor-dispatcher-coupling.md) | Processor enabled without dispatcher registration | GA |
| [analyzers.transactional-ef-interceptor](transactional-ef-interceptor.md) | Transactional EF storage missing save interceptor | GA |
| [analyzers.transactional-inbox-dbcontext](transactional-inbox-wiring.md) | Transactional inbox store injected without DbContext | GA |
| [analyzers.transactional-outbox-dbcontext](transactional-outbox-wiring.md) | Transactional outbox store injected without DbContext | GA |
| [analyzers.missing-audit-declaration](audit-declaration.md) | Command or query states no audit position | GA |
| [analyzers.untyped-gate-on-result-message](gate-contracts.md) | Untyped gate on a message that produces a result | GA |

## Diagnostic Inventory (LB1001-LB1019)

| ID | Severity | Category | Rule | Capability page |
| --- | --- | --- | --- | --- |
| `LB1001` | Error | `LiteBus.Handlers` | Duplicate `ICommandHandler<TCommand>` registrations for one command type | [handler-duplicates.md](handler-duplicates.md) |
| `LB1002` | Reserved | N/A | Reserved for future duplicate event-handler symmetry | N/A |
| `LB1003` | Warning | `LiteBus.Handlers` | Query or stream query handler depends on side-effecting APIs | [query-handler-purity.md](query-handler-purity.md) |
| `LB1004` | Error | `LiteBus.Inbox` | `ICommand<TResult>` accepted into inbox APIs | [inbox-accept-rules.md](inbox-accept-rules.md) |
| `LB1005` | Error | `LiteBus.Handlers` | Open generic handler exposes unsupported type-parameter count | [open-generic-handlers.md](open-generic-handlers.md) |
| `LB1006` | Reserved | N/A | Reserved, currently not implemented | N/A |
| `LB1007` | Warning | `LiteBus.Contracts` | Handled durable command or event has no contract attribute or registration | [contract-registration.md](contract-registration.md) |
| `LB1008` | Error | `LiteBus.Handlers` | Command type has no main command handler | [handler-coverage.md](handler-coverage.md) |
| `LB1009` | Error | `LiteBus.Handlers` | Query or stream query type has no main handler | [handler-coverage.md](handler-coverage.md) |
| `LB1010` | Error | `LiteBus.Handlers` | Duplicate query or stream query handlers for one query type | [handler-duplicates.md](handler-duplicates.md) |
| `LB1011` | Warning | `LiteBus.Handlers` | Handler tag is not referenced by any mediation tag filter | [handler-tags.md](handler-tags.md) |
| `LB1012` | Warning | `LiteBus.Handlers` | Handler simple name appears in more than one assembly | [cross-assembly-handlers.md](cross-assembly-handlers.md) |
| `LB1013` | Warning | `LiteBus.Outbox` | Constructor injects `ITransactionalOutboxStore` without `DbContext` | [transactional-outbox-wiring.md](transactional-outbox-wiring.md) |
| `LB1014` | Error | `LiteBus.Configuration` | Inbox or outbox processor enabled without dispatcher in same scope | [processor-dispatcher-coupling.md](processor-dispatcher-coupling.md) |
| `LB1015` | Warning | `LiteBus.Configuration` | Transactional EF storage enforces setup without save interceptor | [transactional-ef-interceptor.md](transactional-ef-interceptor.md) |
| `LB1016` | Warning | `LiteBus.Inbox` | Constructor injects `ITransactionalInboxStore` without `DbContext` | [transactional-inbox-wiring.md](transactional-inbox-wiring.md) |
| `LB1017` | Warning | `LiteBus.Contracts` | `[MessageContract]` type has no explicit `Register` or `RegisterFromAssembly` | [contract-registration.md](contract-registration.md) |
| `LB1018` | Warning (off by default) | `LiteBus.Auditing` | Command or query type states no audit position | [audit-declaration.md](audit-declaration.md) |
| `LB1019` | Warning | `LiteBus.Handlers` | Gate uses the untyped contract for a message that produces a result | [gate-contracts.md](gate-contracts.md) |

## Suppression Policy

- Prefer fixing the code path that triggers the diagnostic.
- Suppress only for intentional deviations that have explicit code review approval.
- Keep suppression scope narrow (`#pragma warning disable <ID>` around one block, then restore).

## Deep Docs

- [Analyzers.md](../../reference/analyzers.md)
- [Capability-Catalog.md](../../reference/capability-catalog.md)
