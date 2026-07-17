# LiteBus Documentation

This directory contains the authoritative LiteBus documentation. Pages are versioned with the source, reviewed with API changes, and validated by the repository build. The former GitHub wiki is not a documentation source for v6.

LiteBus v6 targets .NET 10 and remains unreleased. A page that describes a v6 API defines the current branch contract, not a compatibility promise for an earlier package version.

## Directory Layout

| Directory | Content |
| --- | --- |
| `getting-started/` | Installation, first application, working conventions, and recipes |
| `concepts/` | Mediator contracts, handlers, execution context, and dispatch behavior |
| `architecture/` | Layering, package dependencies, API rules, decisions, and hosting model |
| `reliable-messaging/` | Inbox, outbox, saga, transaction, and delivery semantics |
| `integrations/` | Broker, ingress, dispatch, storage, and schema adapter guides |
| `operations/` | Diagnostics, security, performance, runbook, and troubleshooting guidance |
| `testing/` | Application testing, repository test suites, and integration categories |
| `reference/` | Capability inventory, analyzer rules, feature index, and glossary |
| `extending/` | Custom modules, stores, dispatchers, and framework adapters |
| `migration/` | Version upgrades and MediatR migration guidance |
| `roadmap/` | Deferred work and accepted v6 limits |
| `contributing/` | Repository contribution workflow |
| `catalog/` | Contract-level capability pages grouped by LiteBus subsystem |

## Start Here

| Goal | Page |
| --- | --- |
| Install LiteBus and run the first handler | [Getting Started](getting-started/README.md) |
| Run a compile-checked application | [LiteBus Sample](../samples/LiteBus.Sample/README.md) |
| Select packages without widening dependencies | [Dependency Graph](architecture/dependency-graph.md) |
| Review the full v6 feature inventory | [v6 Feature Index](reference/feature-index-v6.md) |
| Find a contract, builder method, or adapter by capability | [Capability Catalog](reference/capability-catalog.md) |
| Upgrade an application to v6 | [Migration Guide v6](migration/v6.md) |
| Diagnose a configuration or runtime failure | [Troubleshooting](operations/troubleshooting.md) |

## Architecture and API Design

- [Architecture](architecture/README.md) defines dependency roles, module registration, manifests, durable processing, and telemetry boundaries.
- [Dependency Graph](architecture/dependency-graph.md) lists package roles and direct references.
- [API Design](architecture/api-design.md) defines semantic input types, metadata variants, method shapes, and naming rules.
- [Architecture Decisions](architecture/decisions.md) records decisions that constrain future work.
- [Extensibility](extending/README.md) covers custom modules, stores, dispatchers, and host adapters.

## Mediator

| Area | Primary Guide | Detailed Catalog |
| --- | --- | --- |
| Commands | [Command Module](concepts/commands.md) | [Command Catalog](catalog/mediator/commands.md) |
| Queries and streaming | [Query Module](concepts/queries.md) | [Query Catalog](catalog/mediator/queries.md) |
| Events and concurrency | [Event Module](concepts/events.md) | [Event Catalog](catalog/mediator/events.md) |
| Handler stages | [Handler Pipeline](concepts/handler-pipeline.md) | [Pipeline Catalog](catalog/mediator/handler-pipeline.md) |
| Runtime selection | [Handler Filtering](concepts/handler-filtering.md) | [Filtering Catalog](catalog/mediator/handler-filtering.md) |
| Type resolution | [Polymorphic Dispatch](concepts/polymorphic-dispatch.md) | [Resolution Catalog](catalog/runtime/message-resolution.md) |
| Open generic handlers | [Open Generic Handlers](concepts/open-generic-handlers.md) | [Open Generic Catalog](catalog/mediator/open-generic-handlers.md) |

## Inbox and Outbox

Read [Reliable Messaging Semantics](reliable-messaging/semantics.md) before selecting a durable topology.

| Area | Guide | Detailed Catalog |
| --- | --- | --- |
| Inbox acceptance and execution | [Inbox](reliable-messaging/inbox.md) | [Inbox Acceptance](catalog/durable-core/inbox-acceptance.md) and [Inbox Processor](catalog/durable-core/inbox-processor.md) |
| Outbox enqueue and publication | [Outbox](reliable-messaging/outbox.md) | [Outbox Enqueue](catalog/durable-core/outbox-enqueue.md) and [Outbox Processor](catalog/durable-core/outbox-processor.md) |
| Atomic application writes | [Transactional Messaging Writes](reliable-messaging/transactional-writes.md) | [Transactional Writes Catalog](catalog/durable-core/transactional-writes.md) |
| Retry, lease, and dead-letter behavior | [Reliable Messaging](reliable-messaging/README.md) | [Lease, Retry, and Dead Letter](catalog/durable-core/lease-retry-dead-letter.md) |
| Operations | [Operations and Management](operations/README.md) | [Operations Catalog](catalog/durable-core/operations-management.md) |

## Storage

| Adapter | Inbox | Outbox | Shared Infrastructure |
| --- | --- | --- | --- |
| PostgreSQL | `LiteBus.Inbox.Storage.PostgreSql` | `LiteBus.Outbox.Storage.PostgreSql` | [PostgreSQL Schema Management](integrations/postgresql-schema-management.md) |
| Entity Framework Core | [Inbox EF Core Storage](integrations/inbox-ef-core-storage.md) | [Outbox EF Core Storage](integrations/outbox-ef-core-storage.md) | [EF Core Storage Catalog](catalog/storage/storage.efcore.shared-infra.md) |
| In-memory | `LiteBus.Inbox.Storage.InMemory` | `LiteBus.Outbox.Storage.InMemory` | Test and local behavior use only |

The [Storage Catalog](catalog/storage/README.md) maps store roles to each adapter implementation.

## Transport, Dispatch, and Ingress

Transport publishes and consumes wire envelopes. Dispatch maps a leased inbox or outbox envelope to a transport. Ingress maps a consumed transport envelope into inbox acceptance.

| Broker | Transport | Dispatch | Ingress | v6 Classification |
| --- | --- | --- | --- | --- |
| AMQP | [AMQP Transport](integrations/amqp.md) | Inbox and outbox | [AMQP Ingress](integrations/inbox-amqp-ingress.md) | Release target |
| Kafka | [Kafka Transport](integrations/kafka.md) | Inbox and outbox | Kafka ingress | Ingress remains prerelease pending broader failure testing |
| AWS SQS | [AWS SQS Transport](integrations/aws-sqs.md) | Inbox and outbox | AWS SQS ingress | Prerelease pending live-service certification |
| Azure Service Bus | [Azure Service Bus Transport](integrations/azure-service-bus.md) | Inbox and outbox | Azure Service Bus ingress | Prerelease pending live-service certification |
| In-memory | Transport test implementation | Inbox and outbox | In-memory ingress | Testing support |

The [Transport](catalog/transport/README.md), [Dispatch](catalog/dispatch/README.md), and [Ingress](catalog/ingress/README.md) catalogs document wire mapping, acknowledgement, retry, and registration behavior.

## Hosting, Diagnostics, and Telemetry

- [Hosted Services](architecture/hosted-services.md) defines startup tasks, background services, diagnostic checks, and host manifests.
- [Diagnostics and Health](operations/diagnostics-and-health.md) defines framework-neutral checks and ASP.NET Core health integration.
- [Production Runbook](operations/runbook.md) covers startup, shutdown, failed-message operations, and schema ownership.
- [Security and Tenancy](operations/security-and-tenancy.md) covers tenant isolation and payload protection boundaries.
- [Hosting Catalog](catalog/hosting/README.md) lists Microsoft DI, Autofac, ASP.NET Core, health, and OpenTelemetry adapters.

## Saga Extension

[Saga](reliable-messaging/saga.md) documents correlated inbox state, optimistic concurrency, tenant scoping, and store selection. The feature is an inbox extension, not a general workflow engine. The [Saga Catalog](catalog/saga/README.md) lists each contract and integration point.

## Testing and Operations

- [Testing](testing/README.md) covers unit and component test patterns.
- [Testing LiteBus](testing/application-testing.md) covers application tests and mediator replacement.
- [Integration Tests](testing/integration-tests.md) maps broker, storage, hosting, and end-to-end suites to CI categories.
- [Performance Considerations](operations/performance.md) records allocation and concurrency constraints.
- [Analyzers](reference/analyzers.md) lists compile-time diagnostics and their v6 behavior.

## Reference

- [Glossary](reference/glossary.md)
- [LiteBus Cheat Sheet](getting-started/cheat-sheet.md)
- [Cookbook and Scenarios](getting-started/cookbook.md)
- [Migration Guides](migration/README.md)
- [Roadmap](roadmap/README.md)
- [Contributing](contributing/README.md)

## Documentation Checks

Run the same repository check used by CI:

```powershell
pwsh ./scripts/Test-Documentation.ps1
```

The check rejects broken relative links, links that escape the repository, wiki references, trailing whitespace, banned Unicode typography, and phrases prohibited by the repository writing rules.
