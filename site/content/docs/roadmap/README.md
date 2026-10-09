# Roadmap

LiteBus stays a semantic mediator for commands, queries, and events, with durable messaging as opt-in packages. This page lists planned work that is not part of the current contract; current behavior lives in the feature guides and the architecture reference.

> **This page is for** developers who plan an integration and need to know what LiteBus does not do yet.
>
> **After reading it** you know which capabilities are planned and which boundaries stay fixed. Propose work through [Contributing](../contributing/README.md).

## Reliability and Recovery

The [Reliability Roadmap](reliability.md) tracks provider conformance, lease recovery, saga transaction boundaries, payload protection, telemetry alignment, NativeAOT, and release provenance.

## Storage and Persistence

- [Deep Storage Deduplication](storage-deduplication.md) for shared PostgreSQL and Entity Framework Core orchestration behind thin inbox and outbox adapters.
- [Marten Transactional Outbox Writer](marten-outbox-writer.md) for a Marten-backed transactional outbox writer that enlists in the caller document session transaction.
- SQL Server inbox and outbox storage adapters using application-owned migrations and the same storage role boundaries as existing relational adapters.
- Contract payload upgrader hooks for in-flight envelopes.

## Dispatch and Ingress

- Per-broker transport identity with keyed publishers, so one composition can host more than one transport module.
- Concurrent delivery handlers for the AMQP, Kafka, SQS and in-memory consumers, so `MaxInFlightMessages` above 1 takes effect there as it does on Azure Service Bus.
- An Autofac equivalent of `GetHostedServiceForBackgroundService<T>()` for manual host execution.
- Event replay through an inbox dispatcher that targets `IEventMediator`.
- Webhook and gRPC outbox dispatch adapters.
- HTTP webhook ingress that validates and accepts payloads into the inbox.
- Provider conformance suites for acknowledgement, requeue, dead-letter, cancellation, shutdown, and publisher confirmation behavior.

## Mediator Extensions

- A FluentValidation bridge for command and query pre-handlers.
- Authorization handlers for permission checks before dispatch.
- Query result caching with explicit invalidation.
- Host-neutral transaction and unit-of-work hooks without ORM dependencies.

## Tooling

- Source-generated message and handler registration as an opt-in alternative to reflection.
- A contract catalog CLI with JSON Schema export.
- NativeAOT smoke builds for mediator, durable, and transport paths.
- Additional analyzers for configuration combinations not covered by the current rules.
- [Integrator Feedback](integrator-feedback.md) for design notes on analyzer coverage and API gaps reported by an integrator.

## Boundaries to Preserve

- Inbox and outbox remain separate durable axes.
- Storage, dispatch, ingress, broker, and host adapters remain independently installable.
- Shared contracts remain free of technology and host framework dependencies.
- Package merges, persisted-format changes, and public mediator return-type changes require explicit decisions and migration documentation.

## Next

- [Reliability Roadmap](reliability.md): provider conformance and recovery work
- [Contributing](../contributing/README.md): proposal and implementation requirements
