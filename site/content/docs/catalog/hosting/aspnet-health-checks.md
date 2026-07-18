# ASP.NET Core Health Checks

## Header

- **ID**: `hosting.aspnet-health-checks`
- **Name**: ASP.NET Core health checks
- **Maturity**: GA
- **Summary**: Bridges LiteBus diagnostic probes into `IHealthCheck` through `AddHealthChecks().AddLiteBus()`.

## What It Does

`LiteBus.Extensions.Diagnostics.HealthChecks` registers `LiteBusHealthCheck` that executes manifest probes using `DiagnosticCheckRunner`. The option `FailHealthWhenNoProbes` controls whether empty probe lists report degraded or healthy. `DiagnosticChecks` configures the timeout and maximum parallel probe count.

The health check maps aggregated diagnostic status into ASP.NET health statuses and includes probe details in health data payload.

## Public Surface

### Registration

- `IHealthChecksBuilder AddLiteBus(Action<LiteBusHealthCheckOptions>? configure = null, string name = "litebus")`

### Configuration

- `LiteBusHealthCheckOptions.FailHealthWhenNoProbes` (default `true`)
- `LiteBusHealthCheckOptions.DiagnosticChecks.Timeout` (default 5 seconds per probe)
- `LiteBusHealthCheckOptions.DiagnosticChecks.MaxParallelism` (default 4)

### Consumer Contracts

- `LiteBusHealthCheck` (`IHealthCheck` implementation)

## Packages

- `LiteBus.Extensions.Diagnostics.HealthChecks`

## Requires

- `hosting.host-manifest`
- `hosting.diagnostic-probes`

## Invariants

- Default registration name is `litebus`.
- Default failure status configured in `AddCheck` is unhealthy.
- Default registration tags are `litebus` and `ready` for host readiness filtering.
- Empty-probe behavior follows configured `FailHealthWhenNoProbes`.

## Non-Goals

- Readiness and liveness split into separate built-in checks.
- Exporter setup for health results.

## Observability

- Health check data contains a `probes` array with per-probe status, description, and data.
- Aggregate mapping:
  - all healthy -> healthy
  - any unhealthy -> unhealthy
  - degraded-only or fail-on-empty path -> degraded

## Test Coverage

### Covered Use Cases

#### `LiteBusHealthCheckIntegrationTests.AddLiteBus_WhenDiagnosticProbeIsUnhealthy_ShouldReportUnhealthyHealthCheck`

- **Use case**: unhealthy probe maps to unhealthy health report
- **Test kind**: Integration
- **Description**: registers one unhealthy diagnostic probe and `AddHealthChecks().AddLiteBus()`
- **Behavior**: executes health check service
- **Expected outcome**: report and `litebus` entry are unhealthy with probe data
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/HealthChecks/LiteBusHealthCheckIntegrationTests.cs`

#### `LiteBusHealthCheckIntegrationTests.AddLiteBus_WhenNoDiagnosticProbesAreRegistered_ShouldReportDegradedHealthCheck`

- **Use case**: default empty-probe policy
- **Test kind**: Integration
- **Description**: health checks configured without diagnostic probes
- **Behavior**: executes health check service
- **Expected outcome**: degraded status with "No LiteBus diagnostic probes are registered."
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/HealthChecks/LiteBusHealthCheckIntegrationTests.cs`

#### `LiteBusHealthCheckIntegrationTests.AddLiteBus_WhenNoDiagnosticProbesAndFailHealthWhenNoProbesIsFalse_ShouldReportHealthyHealthCheck`

- **Use case**: opt-out empty-probe failure policy
- **Test kind**: Integration
- **Description**: sets `FailHealthWhenNoProbes = false`
- **Behavior**: executes health check service
- **Expected outcome**: healthy status and no-probe description
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/HealthChecks/LiteBusHealthCheckIntegrationTests.cs`

#### `LiteBusHealthCheckIntegrationTests.AddLiteBus_WhenProbeExceedsConfiguredTimeout_ShouldReportUnhealthyHealthCheck`

- **Use case**: host-configured probe timeout
- **Test kind**: Integration
- **Description**: runs a cancellation-aware blocking probe with a 20 millisecond timeout
- **Behavior**: executes the health check service through the `ready`-tagged aggregate check
- **Expected outcome**: the LiteBus health entry reports unhealthy without hanging the host
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/HealthChecks/LiteBusHealthCheckIntegrationTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Custom health check registration name collisions | Low | Extension accepts custom name but collision behavior relies on ASP.NET health-check registry. |

### Out-of-Scope Use Cases

- Prometheus/Grafana dashboards for health checks.

## Deep Docs

- [Diagnostics and health](../../operations/diagnostics-and-health.md)
