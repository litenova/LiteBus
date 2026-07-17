# ASP.NET Core Management Endpoints

- **ID**: `hosting.aspnet-management-endpoints`
- **Name**: ASP.NET Core Management Endpoints
- **Maturity**: GA
- **Summary**: Maps authenticated HTTP routes for inbox, outbox, processor control, retention, and host diagnostics.

## What It Does

`AddLiteBusManagementEndpoints` maps operator routes under a configurable prefix. The default prefix is `litebus`. Inbox and outbox route groups are enabled independently based on the managers registered by each durable module.

The endpoints expose manager and processor-control contracts. They do not read store implementation details or broker SDK types, so an in-memory, PostgreSQL, or custom store presents the same HTTP model.

```csharp
services.AddLiteBusManagement(options =>
{
    options.RoutePrefix = "operations/litebus";
    options.AuthorizationPolicy = "LiteBusOperator";
});

app.AddLiteBusManagementEndpoints();
```

Hosts that map through `IEndpointRouteBuilder` can call `AddLiteBusManagementEndpoints` directly. Leading and trailing slashes on `RoutePrefix` are removed before route templates are built.

## Public Surface

| Surface | Role |
| --- | --- |
| `IServiceCollection.AddLiteBusManagement(...)` | Registers one `LiteBusManagementOptions` instance |
| `IEndpointRouteBuilder.AddLiteBusManagementEndpoints(...)` | Maps inbox, outbox, and health route groups |
| `LiteBusManagementOptions.RoutePrefix` | Selects the route prefix; default is `litebus` |
| `LiteBusManagementOptions.AllowAnonymousManagement` | Disables authorization metadata when explicitly set to `true` |
| `LiteBusManagementOptions.AuthorizationPolicy` | Applies a named ASP.NET Core authorization policy |
| `LiteBusManagementOptions.FailHealthWhenNoProbes` | Controls the empty-manifest health result |
| `LiteBusManagementOptions.DefaultDrainTimeout` | Supplies the processor drain timeout when the query omits one |

## Routes

The table uses the default `litebus` prefix. Outbox routes mirror inbox routes with `/outbox` in place of `/inbox`.

| Route | Method | Contract |
| --- | --- | --- |
| `/litebus/inbox/messages` | `GET` | Query a page through `IInboxManager.QueryAsync` |
| `/litebus/inbox/messages/{messageId}` | `GET` | Return one envelope or 404 |
| `/litebus/inbox/messages/requeue` | `POST` | Requeue selected message identifiers |
| `/litebus/inbox/messages/requeue-dead-letters` | `POST` | Requeue all dead-lettered rows through paged manager operations |
| `/litebus/inbox/messages` | `DELETE` | Purge a filtered set; unrestricted purge requires `confirm: true` |
| `/litebus/inbox/status-counts` | `GET` | Return counts grouped by inbox status |
| `/litebus/inbox/schema` | `GET` | Return logical or physical store schema metadata |
| `/litebus/inbox/retention/status` | `GET` | Return the latest retention run status |
| `/litebus/inbox/retention/purge` | `POST` | Run retention immediately |
| `/litebus/inbox/processor/state` | `GET` | Return processor state or 404 when the processor is disabled |
| `/litebus/inbox/processor/pause` | `POST` | Pause the processor after its active pass |
| `/litebus/inbox/processor/resume` | `POST` | Resume processor leasing |
| `/litebus/inbox/processor/drain` | `POST` | Drain once, using `timeoutSeconds` or the configured default |
| `/litebus/health` | `GET` | Run diagnostic checks from `LiteBusHostManifest` |

## Authorization and Safety

Management routes require an authenticated caller by default. When `AuthorizationPolicy` contains a policy name, every route requires that policy. Set `AllowAnonymousManagement` only for a host whose network boundary already limits access, such as an isolated local test host.

Unrestricted purge requests require a JSON body with `confirm` set to `true`. Narrowed purge requests can omit confirmation because their query predicates limit the target set. Manager safety exceptions become HTTP 400 responses; unexpected manager failures become HTTP 500 problem responses.

When an inbox or outbox manager is absent, the entire matching route group returns HTTP 404 with an axis-specific message. Processor routes return HTTP 404 when the manager exists but the matching processor control is not registered.

## Packages

- `LiteBus.Extensions.AspNetCore`

## Requires

- `LiteBus.Extensions.Microsoft.DependencyInjection`
- `IInboxManager` for inbox routes
- `IOutboxManager` for outbox routes
- `IInboxProcessorControl` or `IOutboxProcessorControl` for processor routes
- `LiteBusHostManifest` for `/health`
- ASP.NET Core authentication and authorization unless anonymous management is explicitly enabled

## Observability

The health route returns each diagnostic check's name, status, description, and data. It returns HTTP 200 only when the aggregate result is healthy. Degraded or unhealthy results return HTTP 503.

Management routes do not define a separate meter. Use `LiteBus.Inbox` and `LiteBus.Outbox` metrics for queue depth, processor state, pass outcomes, and retention operations. ASP.NET Core request telemetry records HTTP duration and status at the host boundary.

## Invariants

- Inbox and outbox route groups remain independent.
- Storage adapters remain behind manager interfaces.
- Destructive operations retain confirmation and authorization guards.
- Drain timeout query values must be positive; missing or non-positive values use `DefaultDrainTimeout`.
- Route prefix trimming never creates consecutive slashes.

## Non-Goals

- Authentication provider or token configuration.
- Audit log storage.
- Rate limiting and abuse protection.
- Cross-cluster processor coordination.
- Direct transport consumer controls.

## Test Coverage

### Covered Use Cases

| Test | Proves |
| --- | --- |
| `ManagementEndpointTests.Health_ReturnsDegraded_WhenNoProbesAndFailHealthWhenNoProbesIsTrue` | Empty diagnostic manifests return HTTP 503 when configured to fail |
| `ManagementEndpointTests.Health_ReturnsHealthy_WhenNoProbesAndFailHealthWhenNoProbesIsFalse` | Empty diagnostic manifests return HTTP 200 when explicitly allowed |
| `ManagementEndpointTests.Health_IncludesProbeData_WhenProbeFails` | Health payloads preserve diagnostic data |
| `ManagementEndpointAuthorizationIntegrationTests` | Default authentication, named policies, forbidden responses, and anonymous opt-in |
| `ManagementEndpointPostgreSqlIntegrationTests.QueryInboxMessages_ReturnsPersistedRows` | Query binding reads physical PostgreSQL inbox rows |
| `ManagementEndpointPostgreSqlIntegrationTests.Purge_WithConfirm_DeletesRowsInStore` | Confirmed HTTP purge deletes PostgreSQL rows |
| `ManagementEndpointPostgreSqlIntegrationTests.Health_IncludesRegisteredDiagnosticProbe` | Manifest probes execute through a hosted HTTP request |
| `ManagementEndpointOperationsTests.ManagementRoutes_WithBothAxes_ShouldReturnSuccessfulResponses` | Inbox and outbox query, counts, schema, retention, and dead-letter routes bind to managers |
| `ManagementEndpointOperationsTests.ProcessorRoutes_WithRegisteredControls_ShouldInvokeBothAxes` | State, pause, resume, and drain routes invoke matching controls and forward timeout values |
| `ManagementEndpointOperationsTests.InboxRoutes_WhenInboxIsNotConfigured_ShouldReturnNotFound` | An absent axis uses its route-group fallback without affecting the configured axis |
| `ManagementEndpointOperationsTests.AddLiteBusManagementEndpoints_WithCustomPrefix_ShouldMapOnlyCustomPath` | Slash-delimited custom prefixes replace the default route path |

### Untested Use Cases

- HTTP 500 problem response content from an unexpected manager exception.
- HTTP 404 responses for processor routes when an axis manager exists without a processor control.

### Out-of-Scope Use Cases

- Identity provider integration.
- Operator audit persistence.
- Multi-cluster management federation.

## Deep Docs

- [Operations and Management](../../operations/README.md)
- [Hosted Services](../../architecture/hosted-services.md)
- [Diagnostics and Health](../../operations/diagnostics-and-health.md)
