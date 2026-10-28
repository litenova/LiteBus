# Management Endpoints

`LiteBus.Extensions.AspNetCore` exposes HTTP management endpoints for inbox and outbox message queries, processor control, retention, and diagnostics. After registering `AddLiteBusManagement()`, map routes with `AddLiteBusManagementEndpoints()`.

When an axis is not registered on the host (for example, an inbox-only configuration), requests under that axis prefix return **404 Not Found** with an axis-not-configured message instead of **500 Internal Server Error**.

> **This page is for** operators who map management routes and read processor state.

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Extensions.AspNetCore` | Management routes, health bridge |
| Inbox/outbox + storage as required | Processor control targets |

## Map the Routes

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

Register authentication before you map routes.

## Authorization

When `AllowAnonymousManagement` is `false` (the default) and no `AuthorizationPolicy` is set, routes require the default authorization policy through `RequireAuthorization()`. Whether an unauthenticated caller receives 401 or 403 depends on the host authentication scheme and authorization setup. Configure authentication and either the default policy or a named policy. Set `AllowAnonymousManagement` to `true` only for local demos and isolated test hosts.

## Options Reference

| Property | Default | Notes |
| --- | --- | --- |
| `AllowAnonymousManagement` | `false` | Set `true` only for local demos |
| `AuthorizationPolicy` | `null` | When set, applied to all management routes |
| `FailHealthWhenNoProbes` | `true` | Health fails when no `IDiagnosticCheck` registered |
| `DiagnosticChecks.Timeout` | 5 seconds | Per-probe health timeout |
| `DiagnosticChecks.MaxParallelism` | 4 | Maximum concurrent probes |
| `DiagnosticChecks.RunTimeout` | `null` | Whole-run budget; when `null`, derived from probe count, `MaxParallelism`, and `Timeout` |
| `DefaultDrainTimeout` | 30 seconds | Drain timeout when the request omits an override |
| `MaxDrainTimeout` | 5 minutes | Largest drain timeout accepted from a request |
| `MaxPageSize` | 100 | Largest message-query page accepted from a request |
| `MaxBulkMessageIds` | 1,000 | Largest selective requeue, purge, or query identifier set |
| `RoutePrefix` | `litebus` | Route group prefix |

## Route Inventory

The table uses the default `litebus` prefix. Outbox routes mirror inbox routes with `/outbox` in place of `/inbox`. That gives 13 routes per axis plus the health route. Set `RoutePrefix` to change the prefix on every route.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/litebus/inbox/messages` | Query a page of rows through `IInboxManager.QueryAsync` |
| GET | `/litebus/inbox/messages/{messageId}` | Return one envelope, or 404 when the id is unknown |
| POST | `/litebus/inbox/messages/requeue` | Requeue selected message identifiers from a JSON body |
| POST | `/litebus/inbox/messages/requeue-dead-letters` | Requeue all dead-lettered rows through paged manager operations |
| DELETE | `/litebus/inbox/messages` | Purge a filtered set. Unrestricted purge requires a confirm body |
| GET | `/litebus/inbox/status-counts` | Return counts grouped by `InboxStatus` |
| GET | `/litebus/inbox/schema` | Return store schema metadata (`StoreSchemaInfo`) |
| GET | `/litebus/inbox/retention/status` | Return the latest retention run status (`RetentionRunStatus`) |
| POST | `/litebus/inbox/retention/purge` | Run retention cleanup immediately |
| GET | `/litebus/inbox/processor/state` | Return processor state, or 404 when the processor is disabled |
| POST | `/litebus/inbox/processor/pause` | Pause leasing after the active pass completes |
| POST | `/litebus/inbox/processor/resume` | Resume leasing after a pause |
| POST | `/litebus/inbox/processor/drain` | Stop leasing and run one final pass. Accepts `?timeoutSeconds` |
| GET | `/litebus/health` | Run manifest diagnostic probes. Returns 503 when the aggregate is not healthy |

For field-level request and response detail, see the contributor contract in [ASP.NET Core Management Endpoints](../catalog/hosting/aspnet-management-endpoints.md).

### Query Messages

`GET .../messages` binds `InboxMessageFilter` (or `OutboxMessageFilter`) and pagination from the query string. Repeatable parameters accept multiple values.

| Parameter | Applies to | Notes |
| --- | --- | --- |
| `messageId`, `messageIds` | Both axes | Match one row or an identifier set. Sets larger than `MaxBulkMessageIds` return **400** |
| `statuses` | Both axes | Match `InboxStatus` or `OutboxStatus` values |
| `contractName` | Both axes | Match the stable contract name |
| `topic` | Outbox only | Match the publication topic |
| `correlationId`, `causationId` | Both axes | Match trace identifiers |
| `tenantId` | Both axes | Match the tenant identifier |
| `createdAfter`, `createdBefore` | Both axes | Inclusive `CreatedAt` bounds |
| `pageSize` | Both axes | Default 50. Must be between 1 and `MaxPageSize` |
| `cursor` | Both axes | Opaque cursor from a previous page. Omit for the first page |

Responses carry the page items with `hasMore` and `nextCursor`. Pass `nextCursor` as `cursor` to read the next page.

### Requeue Messages

`POST .../messages/requeue` takes a JSON body with the identifiers to replay:

```json
{ "messageIds": ["3fa85f64-5717-4562-b3fc-2c963f66afa6"] }
```

Identifier sets larger than `MaxBulkMessageIds` (default 1,000) return **400**. The response reports requested and requeued counts. `POST .../messages/requeue-dead-letters` takes no body. It replays every dead-lettered row in pages of 200.

### Purge Messages

`DELETE .../messages` binds the same filter parameters as query. A purge whose filter narrows the target set (identifier, status, contract, topic, trace, tenant, or created bounds) runs without a body. An unrestricted purge requires a JSON body:

```json
{ "confirm": true }
```

Requests without `confirm: true` return **400**. Identifier sets larger than `MaxBulkMessageIds` return **400**.

### Drain the Processor

`POST .../processor/drain` accepts an optional `?timeoutSeconds` override. When the value is missing or non-positive, the endpoint uses `DefaultDrainTimeout` (30 seconds). When the timeout is above `MaxDrainTimeout` (5 minutes), the endpoint returns **400**. When the manager exists but the matching processor control is not registered, processor routes return **404** with a processor-not-enabled message.

### Status Codes

| Code | Meaning |
| --- | --- |
| 200 | The operation completed. The body carries the result |
| 400 | Invalid input (`ArgumentException` mapped to `invalid-request`) or an operator safety rejection such as unconfirmed purge |
| 401 / 403 | Authorization refused the caller (401 or 403 depends on host authentication setup) |
| 404 | Unknown message id, unconfigured axis, or disabled processor control |
| 503 | `GET /litebus/health` ran and the aggregate status is not healthy |
| 500 | Unexpected manager or store failure, returned as `application/problem+json` without exception text |

## Error Responses

Endpoint failures use `application/problem+json` with stable `type`, `title`, `status`, and `detail` values. The `traceId` extension identifies the request in host logs. Responses never include exception messages from managers, stores, or transport adapters. Invalid input and operator safety rejections return **400**. Unexpected failures return **500**.

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| Drain stops new leases; in-flight work completes per processor options | Instant process exit without hosted-service stop |
| Auth enforced when configured | RBAC beyond ASP.NET authentication you configure |

## Operations

| Task | Endpoint / doc |
| --- | --- |
| Stuck `Processing` rows | [Production Runbook](runbook.md) |
| Pre-deploy drain | `POST /litebus/inbox/processor/drain`, then stop host |
| Audit purge | The response reports the deleted count. Rejected purges log a Warning with EventId 4002 |

## Tests

| Scenario | Location |
| --- | --- |
| Purge without confirm to 400 | `LiteBus.Extensions.UnitTests` |
| Redacted failure problem with trace identifier | `LiteBus.Extensions.UnitTests` |
| Outbox routes when outbox not registered to 404 | `LiteBus.Extensions.UnitTests` |
| Anonymous to 401 | `LiteBus.Extensions.IntegrationTests` |
| Manifest probe registration | `LiteBus.Extensions.IntegrationTests` |

## Related Pages

* [Diagnostics and Health](diagnostics-and-health.md)
* [Hosted Services](../architecture/hosted-services.md)
* [Message States and Alerting](message-states.md)
* [Production Runbook](runbook.md)
