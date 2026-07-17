# Hosting Capability Catalog

LiteBus hosting capabilities define how modules become running workloads. Runtime modules register service dependencies and host manifest entries during composition. Host adapters map the manifest to Generic Host lifecycles, ASP.NET Core endpoints, and OpenTelemetry registration.

The axis is intentionally split into small packages. Composition (`AddLiteBus`) stays separate from host bridges, management HTTP endpoints, health checks, and telemetry registration.

## Manifest Model

```text
AddLiteBus(...)
  -> module registry resolves build order
  -> modules register dependencies and manifest entries
  -> LiteBusHostManifest snapshot is registered
  -> host bridges resolve startup tasks, background loops, diagnostic checks
  -> optional ASP.NET health and management endpoints consume the same manifest
```

Manifest entry contracts:

- `IStartupTask`: one-shot startup work
- `IBackgroundService`: long-running loop work
- `IDiagnosticCheck`: framework-neutral readiness probes

Startup tasks run first. Background loops start only after startup tasks complete. Any startup task failure fails host startup.

## Capabilities (16)

| ID | Name | Maturity | Package(s) |
| --- | --- | --- | --- |
| [hosting.add-lite-bus-microsoft-di](add-lite-bus-microsoft-di.md) | AddLiteBus for Microsoft DI | GA | `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` |
| [hosting.add-lite-bus-autofac](add-lite-bus-autofac.md) | AddLiteBus for Autofac | GA | `LiteBus.Runtime.Extensions.Autofac` |
| [hosting.module-registry](module-registry.md) | Module registry and build order | GA | `LiteBus.Runtime` |
| [hosting.host-manifest](host-manifest.md) | LiteBus host manifest snapshot | GA | `LiteBus.Runtime.Abstractions` |
| [hosting.startup-tasks](startup-tasks.md) | One-shot startup tasks | GA | `LiteBus.Runtime.Abstractions` |
| [hosting.background-services](background-services.md) | Long-running background services | GA | `LiteBus.Runtime.Abstractions` |
| [hosting.diagnostic-probes](diagnostic-probes.md) | Framework-neutral diagnostic probes | GA | `LiteBus.Runtime.Abstractions` |
| [hosting.generic-host-orchestrator](generic-host-orchestrator.md) | Generic host orchestrator | GA | `LiteBus.Runtime.Extensions.Hosting` |
| [hosting.microsoft-hosting-bridge](microsoft-hosting-bridge.md) | Microsoft hosting bridge | GA | `LiteBus.Runtime.Extensions.Microsoft.Hosting` |
| [hosting.autofac-hosting-bridge](autofac-hosting-bridge.md) | Autofac hosting bridge | GA | `LiteBus.Runtime.Extensions.Autofac.Hosting` |
| [hosting.manual-host-execution](manual-host-execution.md) | Manual startup and loop execution | GA | `LiteBus.Runtime.Extensions.Hosting` |
| [hosting.aspnet-health-checks](aspnet-health-checks.md) | ASP.NET Core health check bridge | GA | `LiteBus.Extensions.Diagnostics.HealthChecks` |
| [hosting.aspnet-management-endpoints](aspnet-management-endpoints.md) | ASP.NET Core management endpoints | GA | `LiteBus.Extensions.AspNetCore` |
| [hosting.opentelemetry-inbox](opentelemetry-inbox.md) | Inbox OpenTelemetry registration | GA | `LiteBus.Inbox.Extensions.OpenTelemetry` |
| [hosting.opentelemetry-outbox](opentelemetry-outbox.md) | Outbox OpenTelemetry registration | GA | `LiteBus.Outbox.Extensions.OpenTelemetry` |
| [hosting.opentelemetry-transport](opentelemetry-transport.md) | Transport OpenTelemetry registration | GA | `LiteBus.Transport.Extensions.OpenTelemetry` |

## Package Boundaries

- Platform, mediation, durable, core, technology, and feature roles register manifest entries without referencing host frameworks directly.
- Host-adapter packages bridge the manifest to `IHostedService`, ASP.NET endpoints, or OpenTelemetry builders.
- Apps remain responsible for authentication, authorization policy, rate limiting, and exporter setup.

## Test Sources

| Area | Primary test projects |
| --- | --- |
| Runtime hosting contracts and orchestration | `LiteBus.Runtime.UnitTests` |
| Autofac composition | `LiteBus.Extensions.UnitTests/Autofac` |
| ASP.NET health checks | `LiteBus.Extensions.IntegrationTests/HealthChecks` |
| ASP.NET management endpoints | `LiteBus.Extensions.UnitTests/AspNetCore`, `LiteBus.Extensions.IntegrationTests/AspNetCore` |
| OpenTelemetry registration | `LiteBus.Extensions.IntegrationTests/OpenTelemetry` |

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
- [Diagnostics and health](../../operations/diagnostics-and-health.md)
- [Architecture](../../architecture/README.md)
- [Dependency graph](../../architecture/dependency-graph.md)
