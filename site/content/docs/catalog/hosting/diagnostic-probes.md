# Framework-Neutral Diagnostic Probes

## Header

- **ID**: `hosting.diagnostic-probes`
- **Name**: Framework-neutral diagnostic probes
- **Maturity**: GA
- **Summary**: Probe contract and runner used by health checks and management endpoints.

## What It Does

`IDiagnosticCheck` gives LiteBus a host-neutral readiness probe abstraction. Modules register probes with `IModuleConfiguration.RegisterDiagnosticCheck(Type, string)`, and adapters execute them through `DiagnosticCheckRunner`.

`DiagnosticCheckRunner.RunAsync(...)` aggregates status and handles zero-probe policy (`failHealthWhenNoProbes`). Probes run concurrently with a bounded semaphore, and each probe has a timeout. A missing registration, timeout, or probe exception becomes an unhealthy outcome for that probe while sibling probes continue.

## Run Budget

`DiagnosticCheckRunOptions` carries three limits:

| Option | Default | Role |
| --- | --- | --- |
| `MaxParallelism` | `4` | Probes that may execute concurrently |
| `Timeout` | 5 seconds | Maximum time allowed for one probe |
| `RunTimeout` | `null` | Maximum time allowed for the whole run |

A `null` `RunTimeout` derives the budget as the worst-case packing of the probe count over `MaxParallelism` lanes at `Timeout` per probe. An explicit `RunTimeout` cancels the run when it expires and reports unfinished probes as unhealthy. After a computed budget expires, the runner allows a 1-second grace period before reporting over-budget probes, since the computed budget already equals the worst case of the in-flight per-probe timeouts.

## Shipped Probes

| Probe name | Type | Registered by |
| --- | --- | --- |
| `litebus.audit.trail` | `AuditTrailDiagnosticCheck` | Command, query, or event module with auditing enabled |
| `litebus.idempotency.store` | `IdempotencyStoreDiagnosticCheck` | Command module with idempotency enabled |
| `inbox.postgresql.schema` | `PostgreSqlInboxSchemaDiagnosticCheck` | PostgreSQL inbox storage module |
| `outbox.postgresql.schema` | `PostgreSqlOutboxSchemaDiagnosticCheck` | PostgreSQL outbox storage module |
| `idempotency.postgresql.schema` | `PostgreSqlIdempotencySchemaDiagnosticCheck` | PostgreSQL idempotency store module |
| `recurring.postgresql.schema` | `PostgreSqlRecurringLeaseSchemaDiagnosticCheck` | PostgreSQL recurring lease module |
| `transport.amqp.connectivity` | `AmqpConnectivityDiagnosticCheck` | AMQP transport module |
| `transport.kafka.connectivity` | `KafkaConnectivityDiagnosticCheck` | Kafka transport module |
| `transport.sqs.connectivity` | `AwsSqsConnectivityDiagnosticCheck` | SQS transport module |
| `transport.azure_service_bus.connectivity` | `AzureServiceBusConnectivityDiagnosticCheck` | Azure Service Bus transport module |

## Public Surface

### Consumer Contracts

- `IDiagnosticCheck`
- `DiagnosticResult`
- `DiagnosticCheckDescriptor`
- `DiagnosticCheckRunner.RunAsync(...)`
- `DiagnosticCheckRunOptions`

### Registration

- `IModuleConfiguration.RegisterDiagnosticCheck(Type implementationType, string name)`
- Inbox/outbox builder sugar: `AddDiagnosticCheck<TCheck>(string name)`

## Packages

- `LiteBus.Runtime.Abstractions`
- `LiteBus.Runtime` (module configuration implementation)

## Requires

- `hosting.host-manifest`
- `hosting.aspnet-health-checks` or `hosting.aspnet-management-endpoints` for HTTP surfaces

## Invariants

- Descriptor name is the operator-facing probe name.
- Descriptor implementation type must resolve to `IDiagnosticCheck` from DI.
- Registering the same probe type under the same name more than once keeps only the first registration; registering it under a different name fails composition with `ModuleCompositionException`, and registering a name already taken by another probe type fails composition with `ModuleCompositionException`.
- `MaxParallelism` and `Timeout` are positive. A negative `RunTimeout` is rejected when health or management endpoints are registered and when a run starts. Probe cancellation is requested when a timeout expires.
- Caller cancellation propagates; provider failures, missing registrations, and timeouts become isolated unhealthy outcomes.

## Non-Goals

- Automatic probe retries.
- Built-in policy engine for probe-specific thresholds.

## Observability

Probe results include:

- aggregate status: healthy, degraded, unhealthy
- per-probe name, status, description
- optional structured `Data` payload

When no probes are registered and fail-on-empty is true, runner returns degraded with synthetic `litebus.probes` result.

## Test Coverage

### Covered Use Cases

#### `DiagnosticCheckRunnerTests.RunAsync_WhenNoProbesAndFailHealthWhenNoProbesIsTrue_ShouldReportDegraded`

- **Use case**: fail-on-empty policy
- **Test kind**: Unit
- **Description**: executes runner with empty manifest
- **Behavior**: sets `failHealthWhenNoProbes` to true
- **Expected outcome**: degraded aggregate and synthetic `litebus.probes`
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/DiagnosticCheckRunnerTests.cs`

#### `DiagnosticCheckRunnerTests.RunAsync_WhenProbeIsUnhealthy_ShouldReportUnhealthy`

- **Use case**: unhealthy probe aggregation
- **Test kind**: Unit
- **Description**: one probe returns unhealthy
- **Behavior**: runs diagnostic runner
- **Expected outcome**: aggregate status is unhealthy
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/DiagnosticCheckRunnerTests.cs`

#### `ModuleConfigurationDiagnosticCheckTests.RegisterDiagnosticCheck_SameTypeAndNameTwice_ShouldIgnoreSecondRegistration`

- **Use case**: duplicate probe registration with the same name
- **Test kind**: Unit
- **Description**: registers the same probe type twice under the same name
- **Behavior**: reads diagnostic check descriptors
- **Expected outcome**: only first registration persists
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleConfigurationDiagnosticCheckTests.cs`

#### `ModuleConfigurationDiagnosticCheckTests.RegisterDiagnosticCheck_SameTypeWithDifferentName_ShouldThrowLiteBusConfigurationException`

- **Use case**: duplicate probe type under a different name
- **Test kind**: Unit
- **Description**: registers the same probe type under two different names
- **Behavior**: attempts the conflicting registration at composition
- **Expected outcome**: composition throws `ModuleCompositionException` naming both names
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleConfigurationDiagnosticCheckTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Parallel long-running probe execution behavior | Covered | `DiagnosticCheckRunnerTests` verifies timeout cancellation and sibling-safe failure mapping. |

Host adapters expose the shared run options through `LiteBusHealthCheckOptions.DiagnosticChecks` and `LiteBusManagementOptions.DiagnosticChecks`.

### Out-of-Scope Use Cases

- External probe orchestration across multiple services.

## Deep Docs

- [Diagnostics and health](../../operations/diagnostics-and-health.md)
