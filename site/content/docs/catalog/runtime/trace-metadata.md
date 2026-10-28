# Trace Metadata and Propagation

- **ID**: `runtime.trace-metadata`
- **Name**: Trace metadata and propagation
- **Maturity**: GA
- **Summary**: This capability represents durable trace metadata through `MessageTrace` variants and stable execution-context keys through `MessageTraceContextKeys`.

## What It Does

The runtime captures correlation and distributed tracing fields in durable envelopes and the mediation context:

- `MessageTrace` variants: `None`, `Correlated`, `Workflow`, `Distributed`
- `MessageTraceContextKeys`: correlation, causation, tenant, and trace-context item keys
- `DurableEnvelopeMetadataMapper` maps trace values to and from envelope fields
- `W3CTraceContextParser` accepts a direct W3C trace parent or a JSON object with `traceparent` and optional `tracestate`

## Ambient Trace

`InboxModuleBuilder.UseAmbientTrace()` and `OutboxModuleBuilder.UseAmbientTrace()` stamp envelopes that carry no explicit trace with the trace of the message being handled, through `MessageTrace.FromAmbient()`. The factory reads the ambient correlation id, causation id, and distributed trace context. The incoming message id stored by inbox dispatchers becomes the causation id, and doubles as the correlation id when the ambient correlation id is absent.

The variant follows envelope precedence. It is `Distributed` when a trace context is present alongside correlation and causation, `Workflow` when both ids are known, `Correlated` when only the correlation id is known, and `None` otherwise. Outside a mediation, or with nothing usable in context, the envelope stays untraced. An explicit item trace always takes precedence over the ambient value. Per-call stamping without the module opt-in uses `InboxAcceptItem<TMessage>.FromAmbient(message)` and `OutboxEnqueueItem<TEvent>.FromAmbient(message)`.

## Public Surface

| API | Role |
| --- | --- |
| `MessageTrace.None` / `Correlated` / `Workflow` / `Distributed` | Durable trace value objects |
| `MessageTraceContextKeys.CorrelationId` | Context key |
| `MessageTraceContextKeys.CausationId` | Context key |
| `MessageTraceContextKeys.TenantId` | Context key |
| `MessageTraceContextKeys.TraceContext` | Context key |
| `W3CTraceContextParser.TryParse` | Shared parser used by durable processors and transport tracing |
| `MessageTrace.FromAmbient()` | Builds trace metadata from the ambient execution context, or `None` |
| `InboxModuleBuilder.UseAmbientTrace()` / `OutboxModuleBuilder.UseAmbientTrace()` | Opts untraced accepts and enqueues into ambient stamping |
| `InboxAcceptItem<TMessage>.FromAmbient(message)` / `OutboxEnqueueItem<TEvent>.FromAmbient(message)` | Per-call ambient stamping without the module opt-in |
| `InboxEnvelopeOptions` / `OutboxEnqueueOptions` | Composition-time flags the envelope factories read |

## Packages

- `LiteBus.DurableMessaging.Abstractions`
- `LiteBus.Runtime.Abstractions`
- `LiteBus.Messaging`
- `LiteBus.Inbox` and `LiteBus.Outbox` for the `UseAmbientTrace` builders and envelope options

## Requires

- `runtime.message-module`

## Invariants

- No-trace state is represented by `MessageTrace.None.Instance`.
- Distributed trace shape carries correlation, causation, and trace payload.
- Malformed trace context never prevents message processing. Tracing ignores it and continues without a remote parent.
- An explicit trace takes precedence over the ambient value. Without the opt-in, untraced items stay untraced.

## Non-Goals

- Configuring telemetry exporters.

## Observability

Runtime trace metadata feeds the outer transport and OpenTelemetry layers. The core runtime emits no trace-specific meter.

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

#### `InboxAmbientTraceTests.Untraced_accept_with_the_opt_in_inherits_the_ambient_trace`
- **Test kind**: Unit
- **Expected outcome**: an untraced accept under the opt-in stores the ambient trace
- **Remarks**: `tests/LiteBus.Inbox.UnitTests/AmbientTrace/InboxAmbientTraceTests.cs`

#### `OutboxAmbientTraceTests.Enqueue_inside_a_traced_mediation_should_stamp_ambient_trace_when_opted_in`
- **Test kind**: Unit
- **Expected outcome**: an enqueue inside a traced mediation stamps the ambient trace under the opt-in
- **Remarks**: `tests/LiteBus.Outbox.UnitTests/AmbientTrace/OutboxAmbientTraceTests.cs`

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Direct mapper round-trip tests for trace columns | Medium | Mapper logic is source-verified. Explicit direct tests are limited. |

### Out-of-Scope Use Cases

- Generating trace IDs at run time.
