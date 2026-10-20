# Hosted Services

LiteBus modules register startup work, background loops, and diagnostic probes on a host manifest, and the generic host bridge runs them after `AddLiteBus` builds the manifest.

> **This page is for** developers who register inbox, outbox, or recurring command modules and run them on the generic host.
>
> **After reading it** you can enable processors, set schema startup behavior, and run the same work without the generic host. Continue with [Diagnostics and Health](../operations/diagnostics-and-health.md).

```csharp
builder.Services.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(_ => { });
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(typeof(MyCommandHandler).Assembly));

    liteBus.AddInbox(inbox =>
    {
        inbox.Contracts.Register<MyCommand>("my.command", 1);
        inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
    });
});
```

You register storage, dispatch, and processor hosting inside one builder lambda. The builder records your choices before module build, so the order of calls inside the lambda does not change the resolved path. Startup tasks run sequentially before any background service loop starts.

## Register Work on the Manifest

Modules register host work through `IModuleConfiguration`, and `AddLiteBus` exposes the result as `LiteBusHostManifest`. Use the manifest to see what a composition registered.

| Surface | Responsibility | Package |
| :--- | :--- | :--- |
| `IStartupTask` | One-shot startup work through `RunAsync` | `LiteBus.Runtime.Abstractions` |
| `IModuleConfiguration.RegisterStartupTask` | Manifest entry for startup tasks | `LiteBus.Runtime` |
| `IBackgroundService` | Long-running loop through `ExecuteAsync` | `LiteBus.Runtime.Abstractions` |
| `IModuleConfiguration.RegisterBackgroundService` | Manifest entry for background services | `LiteBus.Runtime` |
| `IDiagnosticCheck` | Framework-neutral readiness probe | `LiteBus.Runtime.Abstractions` |
| `IModuleConfiguration.RegisterDiagnosticCheck` | Manifest entry used by module `Build` methods | `LiteBus.Runtime` |
| `LiteBusHostManifest` | Resolved manifest after module build | `LiteBus.Runtime.Abstractions` |
| Generic host bridge | Maps manifest entries to one `IHostedService` orchestrator | `LiteBus.Runtime.Extensions.Hosting`, `LiteBus.Runtime.Extensions.Microsoft.Hosting`, `LiteBus.Runtime.Extensions.Autofac.Hosting` |

Compose through parent module builders. Register storage, dispatch, and ingress inside `AddInbox` or `AddOutbox` with `Use*` extensions. Do not register host framework types directly from core or adapter packages.

## Enable the Inbox Processor

```csharp
builder.AddInbox(inbox =>
{
    inbox.Contracts.Register<MyCommand>("my.command", 1);
    inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
    inbox.UseInProcessDispatch();
    inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
});
```

`EnableInboxProcessor` registers `InboxProcessorBackgroundService` on the manifest. You must register one storage module and one dispatcher module on the same builder first. The inbox module throws at build time when the processor is enabled without a dispatcher. `PollInterval` defaults to one second, `StartupDelay` defaults to zero, and `UseAdaptivePolling` defaults to true.

## Enable the Outbox Processor

```csharp
builder.AddOutbox(outbox =>
{
    outbox.Contracts.Register<OrderSubmitted>("orders.events.submitted", 1);
    outbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
    outbox.UseInProcessDispatch();
    outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
});
```

`EnableOutboxProcessor` registers `OutboxProcessorBackgroundService` on the manifest. The same rule applies: register storage and dispatch on the same builder before you enable the processor.

## Control Service Lifetime

Manifest background services and startup tasks are composed as singleton host adapters. Processors open a fresh scope for every leased envelope through `IMessageDispatchScopeFactory`, and `MessageMediator` creates one dispatch scope per mediation and resolves every handler from it.

When your handler needs a `DbContext` or another scoped service, register handlers with scoped lifetime, which is the default from `RegisterFromAssembly`. Do not inject scoped services directly into singleton processor services.

Scoped means per mediation, not per request. The dispatch scope is a sibling of the ambient HTTP request scope, so keep three consequences in mind:

- A scoped `DbContext` in your handler is not the request `DbContext`.
- Two `SendAsync` calls inside one HTTP request get different scoped instances, so one unit of work cannot span two commands.
- A test that resolves a scoped service from its own scope does not touch the instance your handler used.

For per-mediation state, take `IExecutionContext` as a constructor dependency and use its `Data` store. It is registered scoped and follows the message rather than the surrounding scope.

## Configure Storage Schema Startup

```csharp
inbox.UsePostgreSqlStorage(postgres =>
{
    postgres.UseDataSource(dataSource);
    postgres.EnsureSchemaCreationOnStartup();
});
```

`UsePostgreSqlStorage` registers the inbox or outbox schema initializer as `IStartupTask` when schema initialization is enabled, which is the default. `EnsureAsync` creates missing tables and rejects incompatible shapes without converting them. Inbox, outbox, and saga use schema version 1. Apply DDL in your deploy path and call `ValidateAsync` in deploy checks. Call `DisableSchemaInitialization()` when migrations own all DDL. See [PostgreSQL Schema Management](../integrations/postgresql-schema-management.md).

## Consume Broker Messages with Ingress

`UseAmqpIngress` registers `TransportInboxIngressConsumer` by default. Call `ingress.DisableIngressConsumer()` or set `ingress.HostOptions.Enabled = false` to register the handler without a consumer loop. Register `AddAmqpTransport(connectionOptions)` once at the root composition boundary. AMQP ingress and dispatch share its `IMessageConsumer` and `ITransportPublisher`; neither feature adapter creates broker connectivity during `Build()`.

## Send Commands on a Loop

```csharp
registry.AddCommands(commands => commands
    .RegisterFromAssembly(typeof(SendDueRemindersCommand).Assembly)
    .AddRecurring(
        () => new SendDueRemindersCommand { BatchSize = 50 },
        host => host.Interval = TimeSpan.FromMinutes(5)));
```

`CommandModuleBuilder.AddRecurring` from `LiteBus.Commands.Recurring` registers `RecurringCommandBackgroundService<TCommand>` through the manifest, one per registered command type. Each pass opens its own dispatch scope and sends one command. A pass that throws is logged and the loop continues after `ErrorBackoff`, which defaults to five minutes. A pass classified as an optimistic-concurrency conflict by `RecurringCommandHostOptions.IsConcurrencyConflict` is logged at Information (EventId 2104) and waits `Interval`, which defaults to one minute. A result that implements `IRecurringWorkReport` with `HasMoreWork` set to true skips the interval so a backlog clears without idle waits.

The loop runs on every host instance because LiteBus has no leader election. Use it for a command that drains work by leasing rows. Use inbox or outbox processors for work that must be claimed exactly once. See [Recurring Commands](../integrations/recurring-commands.md).

## Run Without the Generic Host

Resolve `InboxProcessorBackgroundService` or any manifest `IBackgroundService` and call `ExecuteAsync` when you do not use the generic host. Resolve schema initializers or any `IStartupTask` and call `RunAsync` for manual startup work.

## Details

| Fact | Value |
| :--- | :--- |
| `IDiagnosticCheck.Name` | Must equal the name passed to `AddDiagnosticCheck<TCheck>(string name)` on the inbox or outbox builder |
| Startup failure | When a startup task throws, host startup fails closed: background services do not start and the exception propagates from `IHostedService.StartAsync` |
| Loop failure | When a LiteBus loop throws unexpectedly, the orchestrator requests `IHostApplicationLifetime.StopApplication()` and rethrows for the host to observe |
| Management and health defaults | `FailHealthWhenNoProbes` defaults to true; probe timeout defaults to five seconds; `MaxParallelism` defaults to four; see [Diagnostics and Health](../operations/diagnostics-and-health.md) |
| Operator queries | `IInboxManager` and `IOutboxManager` expose `GetStatusCountsAsync`, filtered queries, selective requeue, purge guards, and dead-letter replay; see [Operations and Management](../operations/management-endpoints.md) |
| Delayed visibility | Schedule inbox accept or outbox enqueue through `MessageVisibility` on `InboxAcceptMetadata` and `OutboxEnqueueMetadata`, not through separate scheduler interfaces |
| Telemetry names | Instrument names on public telemetry types are stable contract; see [Architecture](README.md) |

## Next

- [Diagnostics and Health](../operations/diagnostics-and-health.md)
- [Operations and Management](../operations/management-endpoints.md)
- [PostgreSQL Schema Management](../integrations/postgresql-schema-management.md)
