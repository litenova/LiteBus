# LiteBus Documentation

LiteBus provides command, query, and event mediation for .NET 10 applications. Durable modules add inbox, outbox, saga, storage, dispatch, ingress, hosting, and operational APIs.

These pages describe the current released API and behavior. Historical API and database transitions belong only in the [Migration Guides](migration/README.md).

## Start Here

| Goal | Page |
| --- | --- |
| Install LiteBus and run a handler | [Getting Started](getting-started/README.md) |
| Run a compile-checked application | [LiteBus Sample](https://github.com/litenova/LiteBus/blob/main/samples/LiteBus.Sample/README.md) |
| Select packages and integrations | [Dependency Graph](architecture/dependency-graph.md) |
| Review supported capabilities | [Feature Index](reference/feature-index-v6.md) |
| Find a contract, builder, or adapter | [Capability Catalog](reference/capability-catalog.md) |
| Diagnose configuration or runtime behavior | [Troubleshooting](operations/troubleshooting.md) |

## Documentation Areas

| Area | Content |
| --- | --- |
| [Concepts](concepts/commands.md) | Commands, queries, events, handler pipelines, filtering, and dispatch |
| [Architecture](architecture/README.md) | Dependency roles, module registration, API rules, hosting, and design decisions |
| [Reliable Messaging](reliable-messaging/README.md) | Inbox, outbox, saga, transactions, retries, leases, and delivery semantics |
| [Integrations](integrations/postgresql-schema-management.md) | Storage, transport, dispatch, ingress, and schema adapters |
| [Operations](operations/README.md) | Diagnostics, security, management, performance, and troubleshooting |
| [Testing](testing/README.md) | Application testing, repository suites, and integration categories |
| [Extending](extending/README.md) | Custom modules, stores, dispatchers, and host adapters |
| [Reference](reference/feature-index-v6.md) | Feature inventory, analyzers, glossary, and semantic validation |
| [Catalog](catalog/mediator/README.md) | Contract-level capability pages grouped by subsystem |
| [Migration](migration/README.md) | Historical version and MediatR migration procedures |
| [Roadmap](roadmap/README.md) | Planned work that is not part of the current product |

## Core Subjects

- [Commands](concepts/commands.md), [Queries](concepts/queries.md), and [Events](concepts/events.md)
- [Handler Pipeline](concepts/handler-pipeline.md) and [Execution Context](concepts/execution-context.md)
- [Inbox](reliable-messaging/inbox.md), [Outbox](reliable-messaging/outbox.md), and [Saga](reliable-messaging/saga.md)
- [Transactional Messaging Writes](reliable-messaging/transactional-writes.md)
- [PostgreSQL Schema Management](integrations/postgresql-schema-management.md)
- [Hosted Services](architecture/hosted-services.md)
- [Diagnostics and Health](operations/diagnostics-and-health.md)
- [Production Runbook](operations/runbook.md)

## Documentation Checks

Run the repository documentation check used by CI:

```powershell
pwsh ./scripts/Test-Documentation.ps1
```

The check rejects broken links, links that escape the repository, wiki references, trailing whitespace, banned Unicode typography, and wording prohibited by the repository writing rules.
