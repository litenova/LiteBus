# Documentation

LiteBus provides command, query, and event mediation for .NET applications. Handlers run in the caller process through ordered pipelines. The inbox, outbox, and saga modules add durable execution, publication, and correlated state when a message has to survive a restart.

> **This page is for** anyone new to LiteBus: a developer registering the mediators, someone deciding whether the durable modules fit, or a contributor.
>
> **After reading it** you know what LiteBus mediates, what durability costs, the words the rest of the pages use, and which page answers your question. To run code, go on to [Getting Started](getting-started/README.md).

These pages describe the current LiteBus release, shown beside the navigation.

## Purpose

A mediator removes the direct call between the code that wants work done and the code that does it, so cross-cutting steps such as validation, auditing, and idempotency attach in one place instead of at every call site.

LiteBus types the three reasons to send a message separately. A command intends one state change, a query reads state and returns a result, and an event reports a fact to zero or more listeners.

When a message has to survive a restart, the inbox accepts it durably and runs it under a lease, the outbox records it beside domain data and publishes it after commit, and sagas keep correlated state across dispatches. Each axis ships as opt-in packages, so a service references only what it runs.

## Capabilities

You register the mediators first, then add durability only where you need it.

- Commands, queries, and events are mediated through ordered pipelines with guards, validators, shortcuts, post-handlers, error handlers, and completion handlers. Per-mediation state travels in the execution context. Start with [Commands](concepts/commands.md), [Queries](concepts/queries.md), [Events](concepts/events.md), and [The Handler Pipeline](concepts/handler-pipeline.md).
- For durable execution and publication, the [inbox](reliable-messaging/inbox.md) accepts commands and runs them under a lease, the [outbox](reliable-messaging/outbox.md) records events beside domain data and publishes them after commit, and [sagas](reliable-messaging/saga.md) keep correlated state across dispatches. [Transactional writes](reliable-messaging/transactional-writes.md) commit domain and message rows together.
- Storage, transport, dispatch, and ingress adapters ship in per-concern packages. There are five transports (RabbitMQ through AMQP, Kafka, AWS SQS, Azure Service Bus, and in-memory) and three store families (PostgreSQL, Entity Framework Core, and in-memory). See [Storage](reference/feature-index.md#storage) and [Ingress](reference/feature-index.md#ingress). [PostgreSQL schema management](integrations/postgresql-schema-management.md) covers migration-owned DDL.
- Operations support comes from the host [manifest](architecture/hosted-services.md) for startup tasks, background loops, and diagnostic probes, [diagnostics and health](operations/diagnostics-and-health.md) endpoints, the [production runbook](operations/runbook.md), and Roslyn [analyzers](reference/analyzers.md) that catch registration and inbox mistakes at compile time.

## What It Costs

Each promise below holds only with the named registration. [Limits and Opt-in Boundaries](reference/limits.md) has the details.

- Mediation alone persists nothing. A process crash mid-handler loses the work. Durable delivery needs a store adapter and a processor host on each axis that must survive one. See [Mediation Alone Persists Nothing](reference/limits.md#mediation-alone-persists-nothing).
- One transport module is allowed per composition. Registering a second broker transport fails composition with an error naming both modules. See [One Transport Module per Composition](reference/limits.md#one-transport-module-per-composition).
- Saga duplicate suppression covers only the latest applied message. An older redelivery can run its handler again after a newer message advances the row. Handlers with external side effects need their own idempotency. See [Saga Duplicate Suppression Covers the Latest Message](reference/limits.md#saga-duplicate-suppression-covers-the-latest-message).
- Lease visibility follows the database clock on stores that have one. The request timestamps are the fallback for stores without one. Clock skew larger than a retry delay changes the retry schedule. See [Lease Visibility Follows the Database Clock](reference/limits.md#lease-visibility-follows-the-database-clock).
- `Safety.MaxInFlightMessages` above 1 takes effect only on Azure Service Bus ingress. The AMQP, Kafka, SQS, and in-memory adapters enforce 1. See [Concurrency Bounds Differ per Adapter](reference/limits.md#concurrency-bounds-differ-per-adapter).

## Words These Pages Use

Each word below names one thing across these pages. The [Glossary](reference/glossary.md) is the full list, and it takes precedence where a page and a type name disagree.

| Word | Meaning |
| --- | --- |
| [Command](reference/glossary.md#core-concepts) | A message that intends one state change, handled by exactly one handler. |
| [Query](reference/glossary.md#core-concepts) | A message that reads state and returns a result, handled by exactly one handler. |
| [Event](reference/glossary.md#core-concepts) | A message that reports a fact, broadcast to zero or more handlers. |
| [Handler](reference/glossary.md#core-concepts) | The class that processes a message. |
| [Pipeline](reference/glossary.md#pipeline) | The pre stage, main handler, post-handlers, error handlers, and completion handlers for one message. |
| [Inbox](reference/glossary.md#reliable-messaging-entry-points) | Storage that accepts command-shaped messages for later execution. |
| [Outbox](reference/glossary.md#reliable-messaging-entry-points) | Storage that records event-shaped messages for later publication. |
| [Envelope](reference/glossary.md) | The stored form of an accepted or enqueued message, with its contract name, payload, lease, and delivery state. |
| [Lease](reference/glossary.md#reliable-messaging-entry-points) | Temporary row ownership so concurrent workers do not process the same envelope twice. |
| [Store](reference/glossary.md) | A persistence adapter behind the inbox, outbox, or saga roles. |
| [Dispatcher](reference/glossary.md#reliable-messaging-entry-points) | The component that runs or publishes a leased envelope. |
| [Ingress](reference/glossary.md) | A broker consumer that accepts deliveries into the inbox. |

## Page Guide

Pick the row that matches your task.

| Goal | Page |
| --- | --- |
| Install LiteBus and run a handler | [Getting Started](getting-started/README.md) |
| Run a compile-checked application | [Sample Application Tour](getting-started/sample.md) |
| Select packages and integrations | [Dependency Graph](architecture/dependency-graph.md) |
| Review supported capabilities | [Feature Index](reference/feature-index.md) |
| Find a contract, builder, or adapter | [Capability Catalog](reference/capability-catalog.md) |
| Diagnose configuration or runtime behavior | [Troubleshooting](operations/troubleshooting.md) |

| Area | Content |
| --- | --- |
| [Concepts](concepts/commands.md) | Commands, queries, events, handler pipelines, filtering, and dispatch |
| [Architecture](architecture/README.md) | Dependency roles, module registration, API rules, hosting, and design decisions |
| [Reliable Messaging](reliable-messaging/README.md) | Inbox, outbox, saga, transactions, retries, leases, and delivery semantics |
| [Integrations](integrations/postgresql-schema-management.md) | Storage, transport, dispatch, ingress, and schema adapters |
| [Operations](operations/README.md) | Diagnostics, security, management, performance, and troubleshooting |
| [Testing](testing/README.md) | Application testing, repository suites, and integration categories |
| [Extending](extending/README.md) | Custom modules, stores, dispatchers, and host adapters |
| [Reference](reference/feature-index.md) | Feature inventory, analyzers, glossary, and limits |
| [Catalog](catalog/mediator/README.md) | Contract-level capability pages grouped by subsystem |
| [Migration](migration/README.md) | Historical version and MediatR migration procedures |
| [Roadmap](roadmap/README.md) | Planned work that is not part of the current product |

## Requirements

You need the .NET 10 SDK and working knowledge of C#.

You need one DI container adapter. That means the Microsoft.Extensions.DependencyInjection or Autofac extension packages for the modules you use.

For durable messaging, you need one store adapter and one enabled processor per axis you run. The exact boundary is on [Limits and Opt-in Boundaries](reference/limits.md).

If you upgrade from the stable release, read the [v7 migration guide](migration/v7.md) before changing registration or storage code.

## Reading These Pages as Plain Text

These pages are also served as plain text, following the [llms.txt convention](https://llmstxt.org), for language models and agents that would otherwise read rendered HTML.

The site root serves `llms.txt` with a one-line description of every page, and `llms-full.txt` with the complete text of those pages in one document. Appending `.md` to any documentation URL returns that page's Markdown source.

## Next

- [Getting Started](getting-started/README.md) to register LiteBus and send your first messages
- [Feature Index](reference/feature-index.md) to review supported capabilities
- [Troubleshooting](operations/troubleshooting.md) when a message fails to mediate
