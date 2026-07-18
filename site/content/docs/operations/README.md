# Operations and Management

**Production tier: GA**

`LiteBus.Extensions.AspNetCore` exposes HTTP management endpoints for inbox/outbox processors, retention, and diagnostics. Map routes with `AddLiteBusManagementEndpoints()` after registering `AddLiteBusManagement()`.

When an axis is not registered on the host (for example inbox-only configuration), requests under that axis prefix return **404 Not Found** with an axis-not-configured message instead of **500 Internal Server Error**.

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Extensions.AspNetCore` | Management routes, health bridge |
| Inbox/outbox + storage as required | Processor control targets |

## Registration

```csharp
builder.Services.AddLiteBusManagement(options =>
{
    options.AuthorizationPolicy = "LiteBusOperator";
    options.FailHealthWhenNoProbes = true;
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.AddLiteBusManagementEndpoints();
app.Run();
```

Register authentication before mapping routes. When `AllowAnonymousManagement` is `false` (default), unauthenticated callers receive **401** unless you assign `AuthorizationPolicy`.

## Options Reference

| Property | Default | Notes |
| --- | --- | --- |
| `AllowAnonymousManagement` | `false` | Set `true` only for local demos |
| `AuthorizationPolicy` | `null` | When set, applied to all management routes |
| `FailHealthWhenNoProbes` | `true` | Health fails when no `IDiagnosticCheck` registered |
| `DiagnosticChecks.Timeout` | 5 seconds | Per-probe health timeout |
| `DiagnosticChecks.MaxParallelism` | 4 | Maximum concurrent probes |
| `DefaultDrainTimeout` | 30 seconds | Drain timeout when the request omits an override |
| `MaxDrainTimeout` | 5 minutes | Largest drain timeout accepted from a request |
| `MaxPageSize` | 100 | Largest message-query page accepted from a request |
| `MaxBulkMessageIds` | 1,000 | Largest selective requeue or purge identifier set |
| `RoutePrefix` | `litebus` | Route group prefix |

## Endpoints (Summary)

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/inbox/messages` | Query inbox rows |
| DELETE | `/inbox/messages` | Purge with filter; requires confirm body when unrestricted |
| POST | `/inbox/processor/drain` | Stop leasing; finish in-flight |
| POST | `/inbox/processor/pause` / `resume` | Processor control |
| POST | `/inbox/retention/purge` | Run retention cleanup |
| GET/DELETE/POST | `/outbox/...` | Outbox equivalents |

### Purge Confirmation (v6)

Unrestricted purge (no narrowing query parameters) requires JSON body:

```json
{ "confirm": true }
```

Requests without `confirm: true` return **400**. Narrow filters (status, age, tenant) may purge without the body when the filter is explicit.

### Error Responses

Endpoint failures use `application/problem+json` with stable `type`, `title`, `status`, and
`detail` values. The `traceId` extension identifies the request in host logs. Responses never
include exception messages from managers, stores, or transport adapters. Invalid input and
operator safety rejections return **400**; unexpected failures return **500**.

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| Drain stops new leases; in-flight work completes per processor options | Instant process exit without hosted-service stop |
| Auth enforced when configured | RBAC beyond ASP.NET authentication you configure |

## Operations

| Task | Endpoint / doc |
| --- | --- |
| Stuck `Processing` rows | [Production runbook](runbook.md) |
| Pre-deploy drain | `POST .../processor/drain`, then stop host |
| Audit purge | Logged at Information; use confirm body in automation |

## Tests

| Scenario | Location |
| --- | --- |
| Purge without confirm to 400 | `LiteBus.Extensions.AspNetCore.UnitTests` |
| Redacted failure problem with trace identifier | `LiteBus.Extensions.AspNetCore.UnitTests` |
| Outbox routes when outbox not registered to 404 | `LiteBus.Extensions.AspNetCore.UnitTests` |
| Anonymous to 401 | `LiteBus.Extensions.AspNetCore.IntegrationTests` |
| Manifest probe registration | `LiteBus.Extensions.Diagnostics.HealthChecks.IntegrationTests` |

## Related Docs

* [Diagnostics and health](diagnostics-and-health.md)
* [Hosted services](../architecture/hosted-services.md)
* [Production runbook](runbook.md)
