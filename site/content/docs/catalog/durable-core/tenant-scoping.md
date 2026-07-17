# Tenant Scoping

**ID:** `durable-core.tenant-scoping`
**Maturity:** GA

## Summary

Tenant metadata partitions durable idempotency, store queries, processor leasing, saga correlation, and optional transport routing. LiteBus carries the tenant identifier; the application remains responsible for authentication and authorization.

## Tenant Model

`TenantScope` makes the command boundary explicit:

- `TenantScope.Unscoped.Instance` represents a message without a tenant partition.
- `new TenantScope.Isolated(tenantId)` represents one named tenant partition.
- `InboxAcceptMetadata.Tenant` and `OutboxEnqueueMetadata.Tenant` carry the scope into durable envelopes.
- `InboxReceipt.Tenant` and `OutboxReceipt.Tenant` expose the persisted scope to callers.

For example, an inbox command can be accepted into `tenant-a` without adding a tenant parameter to `AcceptAsync`:

```csharp
var item = InboxAcceptItem<CreateOrder>.From(
    command,
    InboxAcceptMetadata.Immediate with
    {
        Tenant = new TenantScope.Isolated("tenant-a")
    });

await inbox.AcceptAsync(item, cancellationToken).ConfigureAwait(false);
```

`InboxAcceptItem<TMessage>.WithTenant(message, tenantId)` provides the same composition for the common inbox case.

## Durable Partition Semantics

Built-in stores use the tenant identifier as part of the idempotency scope. The key `create-order-42` in `tenant-a` does not conflict with the same key in `tenant-b`. Reusing both the tenant identifier and key resolves to the existing durable row.

`DurableTenantId.Normalize` maps null and whitespace identifiers to the empty unscoped partition used by durable idempotency indexes. Public receipts map that representation back to `TenantScope.Unscoped`.

The query and purge filters use different null semantics:

- `TenantId = "tenant-a"` restricts the operation to `tenant-a`.
- `TenantId = null` omits the predicate and includes every tenant.

This distinction lets an operator query all rows while preserving an explicit unscoped partition for message identity.

## Processor Leasing

`InboxProcessorOptions.TenantId` and `OutboxProcessorOptions.TenantId` restrict lease requests for one processor instance. The option affects leasing, not acceptance or enqueue.

```csharp
inbox.UseProcessorOptions(new InboxProcessorOptions
{
    TenantId = "tenant-a",
    BatchSize = 50,
    DispatcherConcurrency = 4
});
```

A processor with this configuration leaves `tenant-b` rows pending. A separate processor configured for `tenant-b` can lease those rows from the same store.

Processor leasing belongs to the inbox and outbox domains. It does not depend on transport routing abstractions.

## Ingress Trust Boundary

Transport ingress reads `TransportHeaders.TenantId`, whose canonical wire name is `tenant-id`, only when `TransportInboxIngressOptions.TrustApplicationHeaders` is `true`. An untrusted binding ignores the header and accepts the message as unscoped.

Enable trusted application headers only after the broker binding authenticates the producer and the authorization callback approves the delivery. LiteBus does not derive a tenant from `HttpContext`, claims, or broker credentials.

## Transport Routing

`ITenantRoutingStrategy.ResolveRoute` can compute a broker route from the tenant identifier, stable contract name, and topic hint. Both transport dispatchers use the resolved value as `TransportPublishRequest.Route`; `DefaultDestination` remains unchanged.

```csharp
public sealed class TenantRouteStrategy : ITenantRoutingStrategy
{
    public string ResolveRoute(string? tenantId, string contractName, string? topic)
    {
        var partition = string.IsNullOrWhiteSpace(tenantId) ? "shared" : tenantId;
        return $"{partition}.{topic ?? contractName}";
    }
}
```

Without a strategy, inbox dispatch uses its configured route resolver or contract name. Outbox dispatch prefers `OutboxEnvelope.Topic`, then its configured route resolver, then the contract name.

## Management Operations

`InboxMessageFilter.TenantId`, `OutboxMessageFilter.TenantId`, `SagaQueryFilter.TenantId`, and their purge counterparts support tenant-restricted operator work. The ASP.NET management endpoint query binding accepts `tenantId` on inbox and outbox message routes.

For example, `GET /litebus/inbox/messages?tenantId=tenant-a&status=Pending` returns pending inbox rows for one tenant when the management endpoints are installed.

## Packages

| Package | Tenant Surface |
| --- | --- |
| `LiteBus.DurableMessaging.Abstractions` | `TenantScope`, `DurableTenantId`, and durable metadata |
| `LiteBus.Inbox.Abstractions` | Accept metadata, receipts, lease requests, and manager filters |
| `LiteBus.Outbox.Abstractions` | Enqueue metadata, receipts, lease requests, and manager filters |
| `LiteBus.Saga.Abstractions` | Tenant-scoped saga correlation, query, and purge filters |
| `LiteBus.Inbox.Ingress` | Trusted `tenant-id` header mapping |
| `LiteBus.Transport.Abstractions` | `ITenantRoutingStrategy` and `TransportHeaders.TenantId` |
| Built-in storage adapters | Tenant persistence, idempotency, query, purge, and lease predicates |

## Invariants

- The same idempotency key can identify one row per tenant partition.
- A configured processor tenant limits only that processor's lease requests.
- A null manager tenant filter includes all tenants.
- Tenant ingress headers are ignored unless application headers are trusted.
- Saga correlation includes the normalized tenant identifier.
- Transport routing changes the route, not the configured destination.

## Non-Goals

- Tenant authentication or authorization policy.
- Automatic tenant discovery from an HTTP or broker principal.
- Database row-level security configuration.
- Cross-tenant dead-letter replay without an explicit operator filter.
- A tenant identifier tag on public metrics, which could create unbounded cardinality.

## Observability

Inbox and outbox meters do not attach tenant identifiers. Use manager filters for per-tenant queue depth and status inspection. Transport activities and logs expose the selected route according to the installed adapter, while canonical transport headers carry `tenant-id` across trusted LiteBus boundaries.

## Test Coverage

The tenant contract is covered at each boundary:

- `InboxStoreContractTests` and `OutboxStoreContractTests` verify tenant-filtered leasing and querying. The inbox contract also verifies deduplication within a tenant and independence across tenants.
- `TenantScopedInboxProcessorTests.ProcessPendingAsync_WithTenantFilters_ShouldIsolateProcessorPasses` runs two complete processor passes against one store and proves each processor leases only its configured tenant.
- `TenantLeaseFilterTests` verifies inbox and outbox lease requests against mixed-tenant in-memory rows.
- `TransportInboxIngressMapperTests.ToInboxAcceptMetadata_ShouldMapOptionalHeadersWhenTrusted` verifies trusted header propagation; companion tests verify untrusted headers are ignored.
- `TransportInboxDispatcherTests` and `TransportOutboxDispatcherTests` verify `ITenantRoutingStrategy` receives the envelope tenant and controls the published route.
- Broker dispatch integration tests verify `tenant-id` propagation for AMQP, Kafka, Azure Service Bus, AWS SQS, and the in-memory transport.
- `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_WithTenantScopedAccepts_ShouldPersistSeparateSagaRows` verifies two tenants with the same saga correlation create separate PostgreSQL rows.

The remaining low-priority stress case is fairness across a large number of tenant-specific processor instances. Correct partition filtering is covered; throughput depends on the store, batch size, worker count, and broker topology selected by the application.

## Related Documentation

- [Security and Tenancy](../../operations/security-and-tenancy.md)
- [Inbox](../../reliable-messaging/inbox.md)
- [Outbox](../../reliable-messaging/outbox.md)
- [Saga](../../reliable-messaging/saga.md)
- [Tenant Routing](../transport/tenant-routing.md)
- [Management Endpoints](../hosting/aspnet-management-endpoints.md)
