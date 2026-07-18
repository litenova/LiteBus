# Trace Metadata and Propagation

- **ID**: `runtime.trace-metadata`
- **Name**: Trace metadata and propagation
- **Maturity**: GA
- **Summary**: Represents durable trace metadata via `MessageTrace` variants and stable execution-context keys via `MessageTraceContextKeys`.

## What It Does

Runtime captures correlation and distributed tracing fields in durable envelopes and mediation context:

- `MessageTrace` variants: `None`, `Correlated`, `Workflow`, `Distributed`
- `MessageTraceContextKeys`: correlation, causation, tenant, and trace-context item keys
- `DurableEnvelopeMetadataMapper` maps trace values to and from envelope fields
- `W3CTraceContextParser` accepts a direct W3C trace parent or a JSON object with `traceparent` and optional `tracestate`

## Public Surface

| API | Role |
| --- | --- |
| `MessageTrace.None` / `Correlated` / `Workflow` / `Distributed` | Durable trace value objects |
| `MessageTraceContextKeys.CorrelationId` | Context key |
| `MessageTraceContextKeys.CausationId` | Context key |
| `MessageTraceContextKeys.TenantId` | Context key |
| `MessageTraceContextKeys.TraceContext` | Context key |
| `W3CTraceContextParser.TryParse` | Shared parser used by durable processors and transport tracing |

## Packages

- `LiteBus.Messaging.Abstractions`
- `LiteBus.Messaging`

## Requires

- `runtime.message-module`

## Invariants

- No-trace state is represented by `MessageTrace.None.Instance`.
- Distributed trace shape carries correlation, causation, and trace payload.
- Malformed trace context never prevents message processing; tracing ignores it and continues without a remote parent.

## Non-Goals

- Telemetry exporter configuration.

## Observability

Runtime trace metadata feeds outer transport and OpenTelemetry layers; core runtime emits no trace-specific meter.

## Test Coverage

### Covered Use Cases

#### `DurableMessagingValueObjectTests.MessageTrace_None_ShouldExposeSingletonInstance`
- **Test kind**: Unit
- **Expected outcome**: no-trace singleton semantics hold

#### `DurableMessagingValueObjectTests.MessageTrace_Workflow_ShouldCarryCorrelationAndCausationIds`
- **Test kind**: Unit
- **Expected outcome**: workflow trace fields are preserved

#### `DurableMessagingValueObjectTests.MessageTrace_Distributed_ShouldCarryFullTracePayload`
- **Test kind**: Unit
- **Expected outcome**: distributed trace payload is preserved

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Direct mapper round-trip tests for trace columns | Medium | Mapper logic is source-verified; explicit direct tests are limited |

### Out-of-Scope Use Cases

- Runtime generation of trace IDs.
