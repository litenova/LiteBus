# Threat model

## What this project does and where untrusted input enters

LiteBus is a .NET library for command, query, and event mediation, with optional durable inbox and outbox, saga, storage (PostgreSQL, EF Core, in-memory, and others), transport (AMQP, Kafka, AWS SQS, Azure Service Bus), hosting, and ASP.NET Core management modules. It runs inside the consuming application's process; it is not a server on its own.

Treat these as untrusted:

- **Broker messages.** Inbox ingress (`src/LiteBus.Inbox.Ingress.*`) and the transport consumers (`src/LiteBus.Transport.*`) read message bodies, headers, contract names, contract versions, idempotency keys, tenant ids, and trace context from a broker that other services, or an attacker with publish rights, can write to. Payloads are deserialized by `src/LiteBus.Messaging/Serialization/SystemTextJsonMessageSerializer.cs` into the CLR type the contract registry maps the contract name to.
- **Management HTTP endpoints.** `src/LiteBus.Extensions.AspNetCore` maps query, requeue, purge, retention, and processor-control endpoints. They require authorization by default (`ApplyManagementAuthorization`); query strings, page cursors (`*MessagePageCursor`), and request bodies are untrusted.
- **Persisted rows.** Inbox and outbox rows written by one tenant or service are read and dispatched by another processor instance; row contents reaching the dispatcher should be treated as untrusted. That includes the outbox `handler` column that targets one handler for per-handler delivery, and idempotency key rows (key, fingerprint, stored result payload) in `litebus_idempotency_keys` and the Marten idempotency documents.
- **The `Idempotency-Key` HTTP header.** `WithIdempotencyKey` in `src/LiteBus.Extensions.AspNetCore` copies a client-supplied header into the command pipeline as the idempotency key. A client controls the key and, through the request body, the fingerprint; it must not be able to read another caller's stored result, collide with another tenant's or another command's key, or exhaust the store.

Trusted: application code that registers handlers and modules, configuration and options objects (schema and table names, connection strings), and the CLR types the application registers as message contracts.

## Components that matter most / least

Most important:

- Contract resolution and payload deserialization: a broker message must never cause instantiation of a type that the application did not register as a contract.
- Inbox ingress and acceptance, idempotency resolution, and tenant scoping (`src/LiteBus.Inbox*`, `src/LiteBus.Outbox*`, `PostgreSqlIdempotencyResolution`): a message from one tenant must not be deduplicated against, leased with, or read as another tenant's message.
- SQL composition in the PostgreSQL stores (`src/LiteBus.*.Storage.PostgreSql`, `src/LiteBus.Storage.PostgreSql`): values must always be parameters, never interpolated.
- Payload encryption (`IPayloadEncryptor`, `InboxPayloadProtector`, outbox payload protection).
- Lease ownership and lease generation in the processors: a stale lease holder must not overwrite the state a current holder wrote.
- Management endpoint authorization and purge confirmation.
- Command idempotency (`src/LiteBus.Commands/Idempotency`, `src/LiteBus.Commands.Storage.PostgreSql`, `src/LiteBus.Commands.Storage.Marten`, `InMemoryIdempotencyStore`): two concurrent claims of one key must never both run the handler, a claim taken over after its timeout must not let a late completion corrupt the new claimant's result, a stored result must only be replayed to a request with the same key and a matching fingerprint, and keys must be scoped so one command or tenant cannot replay another's result.
- Leases outside the processors: the PostgreSQL recurring command lease (`src/LiteBus.Commands.Recurring.Storage.PostgreSql`) must keep two instances from running one pass at once, including across renewal failures and expiry.
- Fault-to-refusal classification (`IFaultRefusalClassifier`, `PostgreSqlConstraintRefusalClassifier`): a classified fault must not leak exception details that were not meant for the caller, and must not turn a fault into a success.

Less important: OpenTelemetry and diagnostics adapters, the in-memory stores and transports (development and tests).

Out of scope: `src/LiteBus.Analyzers` (compile-time Roslyn analyzers), `samples/`, `benchmarks/`, `site/` (the documentation website), `scripts/`, and `tests/`.

## How to exercise it

The image builds the whole solution (`LiteBus.slnx`) and runs the unit test projects in `tests/*.UnitTests`. They compose the mediator and the durable modules with in-memory stores and transports and are the quickest drivers for pipeline, inbox, outbox, and processor behaviour (for example `tests/LiteBus.Inbox.UnitTests`, `tests/LiteBus.Outbox.UnitTests`, `tests/LiteBus.Mediator.UnitTests`). Command idempotency is driven end to end by `tests/LiteBus.Mediator.UnitTests/Idempotency` with `InMemoryIdempotencyStore`, the `Idempotency-Key` header path by `tests/LiteBus.Extensions.UnitTests` over an in-process ASP.NET Core test server, the management endpoints by the same project, and per-handler outbox delivery by `tests/LiteBus.Outbox.UnitTests/PerHandlerDelivery`. The integration suites (`tests/*.IntegrationTests`) need PostgreSQL, SQL Server, MySQL, and broker containers and cannot run offline.

## How you rate severity

- Critical: remote code execution or arbitrary type instantiation from a broker message; SQL injection; cross-tenant read or write of messages.
- High: an authorization bypass on the management endpoints; a message silently lost or executed twice through a lease, idempotency, or transaction-boundary defect; payload decryption with the wrong key or tenant context; disclosure of message payloads.
- Medium: denial of service that one broker message or one HTTP request can trigger (unbounded allocation, a processor stuck on a poison message without dead-lettering).
- Low: information leaks in logs or exception messages that do not include payload contents.

## Anything to leave alone

- Management endpoints are meant to be powerful for an authorized operator; requeue and purge doing what they say is not a finding, but reaching them without the configured authorization is.
- `AllowImmediateCommit` and the in-memory stores are documented as development and test settings.
- Throughput and allocation findings that are not attacker-triggerable.
