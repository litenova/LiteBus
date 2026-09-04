# Hosted Services

LiteBus separates dependency registration from host execution through a **manifest model**. Modules register startup tasks, background services, and diagnostic probes on `IModuleConfiguration`; the generic host bridge runs them after `AddLiteBus` builds the manifest.

| Surface | Responsibility | Package |
| :--- | :--- | :--- |
| `IStartupTask` | One-shot host startup work (`RunAsync`) | `LiteBus.Runtime.Abstractions` |
| `IModuleConfiguration.RegisterStartupTask` | Manifest entry for startup tasks | `LiteBus.Runtime` |
| `IBackgroundService` | Long-running loop (`ExecuteAsync`) | `LiteBus.Runtime.Abstractions` |
| `IModuleConfiguration.RegisterBackgroundService` | Manifest entry for background services | `LiteBus.Runtime` |
| `IDiagnosticCheck` | Framework-neutral readiness probe | `LiteBus.Runtime.Abstractions` |
| `InboxModuleBuilder.AddDiagnosticCheck` / `OutboxModuleBuilder.AddDiagnosticCheck` | Application-facing probe registration sugar | `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions` |
| `IModuleConfiguration.RegisterDiagnosticCheck` | Internal manifest entry used by module `Build()` implementations | `LiteBus.Runtime` |
| `LiteBusHostManifest` | Resolved manifest after module build | `LiteBus.Runtime.Abstractions` |
| Generic host bridge | Maps manifest entries to a single `IHostedService` orchestrator | `LiteBus.Runtime.Extensions.Hosting` (shared), `LiteBus.Runtime.Extensions.Microsoft.Hosting`, `LiteBus.Runtime.Extensions.Autofac.Hosting` |

Call `services.AddLiteBus(builder => ...)` using `ILiteBusBuilder` from `LiteBus.Runtime.Composition`. Register storage, dispatch, ingress, and processors inside nested `AddInbox` / `AddOutbox` builders.

## Service Lifetime Constraint

Manifest-registered background services and startup tasks are composed as **singleton** host adapters. Microsoft DI and Autofac integrations register one `IMessageDispatchScopeFactory`, and processors use it to open a fresh scope for every leased envelope.

When handlers need a `DbContext` or another scoped service, register handlers with scoped lifetime (the default from `RegisterFromAssembly`). `MessageMediator` creates one dispatch scope per mediation through `IMessageDispatchScopeFactory` and resolves every handler from it, so `SendAsync` / `PublishAsync` get distinct scoped instances without manual scoping. EF stores use application-registered `IDbContextFactory<TContext>` instances for operation contexts. Do not inject scoped services directly into singleton processor services.

Scoped means per mediation, not per request. The dispatch scope is created from the root `IServiceScopeFactory`, so it is a sibling of the ambient HTTP request scope rather than a child of it. Three consequences are worth knowing before designing around it:

- A scoped `DbContext` injected into a handler is not the request's `DbContext`. Middleware that opens a transaction on the request-scoped context and a handler that writes through its own are working on two connections.
- Two `SendAsync` calls inside one HTTP request get two different scoped instances of everything. A unit of work cannot span two commands, which is consistent with the commit position at `HandlerPriorities.UnitOfWork`: one command, one transaction.
- A test that resolves a scoped service from its own scope to seed or assert on it is not touching the instance the handler used.

The default is a fresh scope rather than a nested one because a mediation from a background loop, an inbox processor, or a saga has no ambient scope to nest in. Nesting cannot be the default without making the mediator's behavior depend on its caller.

For per-mediation state, take `IExecutionContext` as an ordinary constructor dependency and use its `Data` store. It is registered scoped and resolves the context of the mediation in flight, which is state that belongs to the message rather than to whichever scope happens to surround it.

`IDiagnosticCheck.Name` must match the name passed to `AddDiagnosticCheck<TCheck>(string name)` on the inbox or outbox module builder. The health bridge validates the names at runtime.

## Inbox Processor

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(c => c.Register<MyCommandHandler>());

    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<MyCommand>("my.command", 1);
        inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
    });
});
```

`EnableInboxProcessor` registers `InboxProcessorBackgroundService` on the manifest.

## Outbox Processor

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddEvents(e => e.Register<OrderSubmittedEventHandler>());

    builder.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.events.submitted", 1);
        outbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
    });
});
```

## Registration Order Inside the Builder

Configure storage, dispatch, and processor hosting in the same builder lambda. The builder records all choices before module build, so callback order does not change the resolved durable path. Storage modules register schema initializers as `IStartupTask` when schema initialization is enabled. Startup tasks run sequentially before background services.

```csharp
inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
inbox.UseInProcessDispatch();
inbox.EnableInboxProcessor();
```

## PostgreSQL Schema Bootstrap

`UsePostgreSqlStorage` registers `PostgreSqlInboxSchemaInitializer` / `PostgreSqlOutboxSchemaInitializer` as `IStartupTask` when `EnableSchemaInitialization` is enabled (the default).

```csharp
inbox.UsePostgreSqlStorage(postgres =>
{
    postgres.UseDataSource(dataSource);
    postgres.EnsureSchemaCreationOnStartup();
});
```

Inbox, outbox, and saga use schema version 1. `EnsureAsync` creates missing tables and rejects incompatible shapes; it does not convert them. Production deployments should apply DDL and call `ValidateAsync` in deploy checks. Development may use `EnsureSchemaCreationOnStartup()` for new databases. See [PostgreSQL Schema Management](../integrations/postgresql-schema-management.md).

Disable host schema work with `postgres.DisableSchemaInitialization()` when DDL is fully migration-owned.

## AMQP Inbox Ingress

`UseAmqpIngress` registers `TransportInboxIngressConsumer` by default. Use `ingress.DisableIngressConsumer()` or `ingress.HostOptions.Enabled = false` to register the handler without a consumer loop.

Register `AddAmqpTransport(connectionOptions)` once at the root composition boundary. AMQP ingress and dispatch declare that module as a dependency and share its `IMessageConsumer` and `ITransportPublisher`; neither feature adapter creates broker connectivity during `Build()`.

## Manual Execution

Resolve `InboxProcessorBackgroundService` (or any manifest `IBackgroundService`) and call `ExecuteAsync` when you do not use the generic host. Resolve schema initializers (or any `IStartupTask`) and call `RunAsync` for manual startup work.

## Diagnostics and Observability

LiteBus emits OpenTelemetry metrics from inbox, outbox, and transport paths. Register meters with `AddLiteBusInboxMetrics()`, `AddLiteBusOutboxMetrics()`, and `AddLiteBusTransportMetrics()` from the matching `LiteBus.*.Extensions.OpenTelemetry` packages.

`AddLiteBus` registers `LiteBusHostManifest`, which lists startup tasks, background services, and diagnostic probe descriptors.

### Consumer-Owned Diagnostic Probes

```csharp
inbox.AddDiagnosticCheck<PostgreSqlInboxSchemaDiagnosticCheck>("litebus.inbox.schema");
```

Implement `IDiagnosticCheck` in application code for schema validation, broker connectivity, or deployment-specific readiness rules.

### Health Endpoint Behavior

`LiteBus.Extensions.AspNetCore` maps `GET /litebus/health` to manifest probes. `LiteBusManagementOptions.FailHealthWhenNoProbes` defaults to **`true`**: when the manifest has zero probes, the endpoint returns degraded/unhealthy. `AddHealthChecks().AddLiteBus()` uses the same default through `LiteBusHealthCheckOptions.FailHealthWhenNoProbes`. Set both to `false` in local samples so demos work without registering probes. Production templates should keep the default `true` and register at least one probe.

Both host surfaces expose `DiagnosticChecks.Timeout` and `DiagnosticChecks.MaxParallelism`. The Generic Host health registration carries `litebus` and `ready` tags. Broker modules register AMQP, Kafka, SQS, or Azure Service Bus readiness probes; SQS and Azure require an explicit queue or subscription diagnostic target.

Startup tasks run sequentially inside the LiteBus host orchestrator before any background service loop starts. When a startup task throws, host startup fails closed: the orchestrator does not start background services and the exception propagates from `IHostedService.StartAsync`.

The orchestrator derives from the .NET Generic Host `BackgroundService` contract and supervises every LiteBus loop. If
one loop throws unexpectedly, it requests `IHostApplicationLifetime.StopApplication()` immediately and rethrows the
failure for the host to observe. Normal shutdown cancellation is suppressed. Keep the Generic Host default
`BackgroundServiceExceptionBehavior.StopHost`; configuring `Ignore` weakens fail-closed supervision for hosted
services outside the LiteBus orchestrator.

See the [.NET 10 `HostOptions.BackgroundServiceExceptionBehavior` contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.hostoptions.backgroundserviceexceptionbehavior?view=net-10.0) for the host-level policy.

```csharp
builder.Services.AddLiteBusManagement(options => options.FailHealthWhenNoProbes = false);
builder.Services.AddHealthChecks().AddLiteBus(options => options.FailHealthWhenNoProbes = false);
app.AddLiteBusManagementEndpoints();
```

Probe `DiagnosticResult.Data` is included in the JSON health response.

### Operator Query APIs

`IInboxManager` and `IOutboxManager` expose `GetStatusCountsAsync`, filtered queries, selective requeue, purge (with confirmation guards), and dead-letter replay.

### ASP.NET Core Management Endpoints

Management routes (`/litebus/inbox/*`, `/litebus/outbox/*`, `/litebus/health`) require authentication by default. Anonymous access is denied unless you set `AllowAnonymousManagement = true` (samples use this in Development only).

```csharp
builder.Services.AddAuthentication().AddJwtBearer(/* ... */);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("LiteBusOperator", policy => policy.RequireRole("operator"));
});
builder.Services.AddLiteBusManagement(options =>
{
    options.AuthorizationPolicy = "LiteBusOperator";
});

app.UseAuthentication();
app.UseAuthorization();
app.AddLiteBusManagementEndpoints();
```

Apply rate limits and audit logging in application code. LiteBus does not ship authentication.

### Deferred Visibility

Schedule inbox accept or outbox enqueue through metadata visibility instead of separate scheduler interfaces:

```csharp
await inbox.AcceptAsync(
    InboxAcceptItem<ShipOrder>.From(
        new ShipOrder(orderId),
        InboxAcceptMetadata.Immediate with
        {
            Visibility = MessageVisibility.After(TimeSpan.FromMinutes(15))
        }));
```

Scheduling and delayed visibility are configured on `InboxAcceptMetadata` and `OutboxEnqueueMetadata` through `MessageVisibility`.

### Shared Contracts Through Messaging Composition

```csharp
services.AddLiteBus(builder =>
{
    builder.AddMessaging(messaging =>
        messaging.Contracts.Register<OrderSubmitted>("orders.events.submitted", 1));
    builder.AddCommands(c => c.RegisterFromAssembly(typeof(MyCommandHandler).Assembly));
    builder.AddEvents(e => e.RegisterFromAssembly(typeof(OrderSubmittedEventHandler).Assembly));

    builder.AddInbox(inbox =>
    {
        inbox.UsePostgreSqlStorage(pg => pg.UseConnectionString(connectionString));
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor();
    });

    builder.AddOutbox(outbox =>
    {
        outbox.UsePostgreSqlStorage(pg => pg.UseConnectionString(connectionString));
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor();
    });
});
```

Autofac hosts use the same `ILiteBusBuilder` overload on `ContainerBuilder.AddLiteBus`.

### OpenTelemetry Operations Examples

| Signal | Suggested alert |
| --- | --- |
| `litebus.inbox.queue.depth` (tag `litebus.inbox.status=Pending`) | Pending depth above SLO for 5 minutes |
| `litebus.inbox.processor.loop_errors` | Any sustained increase |
| `litebus.transport.circuit_breaker.open` | Value `1` for more than one minute |
| Lease-lost / persist-rejected counters | Spike after deploy or DB failover |

Export through your OTLP sink (Prometheus, Azure Monitor, Datadog). Instrument names on public telemetry types are stable contract; do not rename without a major release.

See [Architecture](README.md) for the full metric catalog.
