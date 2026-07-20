# Roadmap

This page lists planned work that is not part of the current LiteBus contract. Current behavior belongs in the feature guides, architecture reference, and capability catalog.

LiteBus remains a semantic mediator for commands, queries, and events. Durable concerns remain opt-in packages with separate inbox, outbox, storage, dispatch, ingress, transport, and host boundaries.

## Reliability and Recovery

The [Reliability Roadmap](reliability.md) tracks provider conformance, lease recovery, saga transaction boundaries, payload protection, telemetry alignment, NativeAOT, and release provenance.

## Storage and Persistence

- [Deep Storage Deduplication](storage-deduplication.md) for shared PostgreSQL and Entity Framework Core orchestration behind thin inbox and outbox adapters.
- SQL Server inbox and outbox storage adapters using application-owned migrations and the same storage role boundaries as existing relational adapters.
- Contract payload upgrader hooks for in-flight envelopes.
- An optional handler-level idempotency record store beyond inbox acceptance metadata.

## Dispatch and Ingress

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

## Boundaries to Preserve

- Inbox and outbox remain separate durable axes.
- Storage, dispatch, ingress, broker, and host adapters remain independently installable.
- Shared contracts remain free of technology and host framework dependencies.
- Package merges, persisted-format changes, and public mediator return-type changes require explicit decisions and migration documentation.

See [Contributing](../contributing/README.md) for proposal and implementation requirements.
