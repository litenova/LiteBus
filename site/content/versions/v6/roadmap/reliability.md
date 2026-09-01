# Reliability Roadmap

This roadmap covers planned reliability, provider parity, operations, NativeAOT, and release-quality work. The listed items are not current LiteBus guarantees until their implementation and documentation are merged.

Independent package boundaries remain a design constraint. Applications must be able to select one durable axis, broker, and store without installing unrelated SDKs.

## Research Basis

- [.NET support policy](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support) defines available runtime support windows.
- [OpenTelemetry messaging semantic conventions](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/) define producer, consumer, process, settle, batch, destination, and context propagation concepts.
- [CloudEvents](https://cloudevents.io/) defines an event envelope and protocol ecosystem for adapter interoperability.
- [.NET Aspire integrations](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/integrations-overview) provide a host-level model for composing databases, brokers, and connection information.

## Priority 0: Correctness and Recovery

### Lease Operations

The current processor assigns monotonically increasing inbox and outbox lease generations, checks owner and generation during renewal and terminal persistence, uses database clocks for relational leases, and reports skipped terminal writes through `PersistResult`.

- Add operator-facing lease history without using configured worker labels as metric dimensions.
- Evaluate explicit graceful-shutdown lease release for work that has not started dispatch.
- Add long-running contention and database failover suites for every supported provider.

### Saga Application Ledger

- Record command application intent and result under the saga correlation key.
- Serialize or conditionally apply concurrent commands at the storage boundary.
- Define one transaction contract for state and inbox terminal persistence where a provider supports it.
- Provide an explicit compensation path where one transaction is unavailable.
- Add crash-window tests around state commit, inbox commit, and process restart.

### Versioned Payload Protection

- Define an encrypted payload envelope with algorithm, key identifier, nonce, authentication tag, and associated-data contract.
- Fail closed on context mismatch and provide a migration tool for stored encrypted envelopes.
- Add key rotation, ciphertext-size, and redacted-diagnostic cases to provider conformance suites.

## Priority 1: Provider and Transport Parity

### Conformance Kit

- Move provider conformance fixtures into a packable `LiteBus.Conformance` package that depends only on abstractions and test framework contracts.
- Require each provider to publish a capability matrix for leases, transactions, strict idempotency, cleanup limits, and cross-process behavior.
- Add a CI matrix for PostgreSQL, Entity Framework Core, SQLite, AMQP, Kafka, Azure Service Bus, and AWS SQS.

### Destination and Safety Contracts

The current ingress API separates provider-neutral admission limits from broker prefetch, receive batching, callback concurrency, and inbox batch size. Every ingress adapter exposes bounded payload, trusted-header, identity, authorization, and batch settings through `TransportInboxIngressSafetyOptions`.

- Introduce typed destination records for Azure topic subscriptions, queue names, Kafka consumer groups, AMQP exchange routes, and SQS queue URLs.
- Add publisher confirmation, dead-letter, requeue, cancellation, and shutdown scenarios to every transport conformance suite.

## Priority 2: Observable Operations

- Add an opt-in OpenTelemetry semantic-convention mode for messaging spans.
- Emit consistent producer, consumer, process, receive, settle, and batch attributes.
- Use links to message creation context for fan-out while retaining broker delivery identity separately.
- Add metrics for lease loss, retry delay, requeue, dead-letter, settlement failure, and queue age with bounded labels.

## Priority 3: Developer Experience

### Source Generation and Trimming

- Add an optional source generator for message contracts and handler descriptors.
- Keep reflection registration as a supported path and annotate reflection-heavy APIs for trimming.
- Add NativeAOT smoke builds for the mediator, in-memory durable path, and one broker adapter.

### Host Composition

- Publish an optional Aspire integration package under the host-adapter role.
- Generate AppHost resource bindings without adding Aspire references to core or abstraction packages.
- Keep the host manifest as the source of startup tasks, background services, and diagnostic checks.

### Documentation and Examples

- Generate package and adapter catalog pages from the dependency graph.
- Add runnable examples for inbox-only, outbox-only, saga with inbox, and one provider from each transport family.

## Priority 4: Supply Chain and Release Quality

- Add a scheduled transitive vulnerability report with a maintainer review policy for high-severity advisories.
- Produce SBOM and provenance attestations for NuGet packages and the documentation site.
- Enforce changed-line coverage with a published baseline.
- Add release smoke tests that install generated packages into a clean consumer sample.

## Decisions to Preserve

- Keep inbox and outbox as separate durable axes.
- Keep storage, dispatch, ingress, broker, and host adapters independently installable.
- Keep per-module Microsoft dependency injection and Autofac packages.
- Keep shared contracts abstract and cross-axis mapping in feature bridges.
- Treat package merges, persisted-format changes, and public mediator return-type changes as versioned decisions with migration procedures.

## Completion Criteria

The roadmap is complete when lease and saga correctness suites pass across supported stores, provider capability matrices are published, messaging telemetry is stable, release artifacts include SBOM and provenance attestations, NativeAOT smoke builds pass, and a clean consumer sample installs only its required packages.
