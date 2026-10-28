# Operations

LiteBus operations cover running LiteBus in production. That means mapping management routes, wiring health probes, reading message states, draining processors before deploys, and triaging failures. This page lists each task with the page that covers it.

## Task Index

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

You pause, resume, and drain processor loops through `IInboxProcessorControl` and `IOutboxProcessorControl`. The management HTTP endpoints wrap these contracts. When you drain from automation, post to `/litebus/inbox/processor/drain` before a deploy. When you drain from application code, inject the control interface and call `DrainAsync`. The [Production Runbook](runbook.md) gives the drain procedure. [Management Endpoints](management-endpoints.md) lists every route.

## Limits

Mediation alone persists nothing. Durable processing needs a store and a processor host on each axis you run. The management routes for an axis return 404 when that axis is not registered. Probes and meters are opt-in per package. Register at least one `IDiagnosticCheck` before relying on health, and add the matching `*.Extensions.OpenTelemetry` package before expecting metrics.
