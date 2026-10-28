# Operations

LiteBus operations cover the management HTTP endpoints, diagnostics and health probes, message states, the production runbook, security and tenancy, performance, and troubleshooting. Start here to find the page for your task.

## Find Your Task

| Task | Page |
| --- | --- |
| Map HTTP routes for processor control, message queries, retention, and diagnostics | [Management Endpoints](management-endpoints.md) |
| Register readiness probes and export metrics | [Diagnostics and Health](diagnostics-and-health.md) |
| Read inbox and outbox status values, query rows, and set alerts | [Message States and Alerting](message-states.md) |
| Triage backlogs, stuck leases, broker failures, and deploy-time drains | [Production Runbook](runbook.md) |
| Configure payload encryption and tenant-scoped leases | [Security and Tenancy](security-and-tenancy.md) |
| Understand mediation cost and keep handlers fast | [Performance Considerations](performance.md) |
| Map an exception or a behavior to its cause and fix | [Troubleshooting](troubleshooting.md) |

## Control Processors

Processor loops support pause, resume, and drain through `IInboxProcessorControl` and `IOutboxProcessorControl`. The management HTTP endpoints wrap these contracts, so automation can use either surface: POST to `/litebus/inbox/processor/drain` before a deploy, or inject the control interface and call `DrainAsync` from application code. See [Production Runbook](runbook.md) for the drain procedure and [Management Endpoints](management-endpoints.md) for the route inventory.

## Limits

Mediation alone persists nothing. Durable processing needs a store and a processor host on each axis you run, and the management routes for an axis return 404 when that axis is not registered. Probes and meters are opt-in per package: register at least one `IDiagnosticCheck` before relying on health, and add the matching `*.Extensions.OpenTelemetry` package before expecting metrics.
