# Tenant Routing

**ID:** `transport.tenant-routing`
**Maturity:** GA

## Summary

`ITenantRoutingStrategy` lets inbox and outbox transport dispatchers compute a publish route from durable tenant metadata without coupling transport packages to inbox or outbox abstractions.

## Public Surface

```csharp
public interface ITenantRoutingStrategy
{
    string ResolveRoute(string? tenantId, string contractName, string? topic);
}
```

The arguments have stable roles:

| Argument | Value |
| --- | --- |
| `tenantId` | Durable envelope tenant, or null for an unscoped message |
| `contractName` | Stable message contract name and version-independent route identity |
| `topic` | Outbox topic when present; inbox dispatch passes the contract name as its topic hint |

The return value becomes `TransportPublishRequest.Route`. The dispatcher keeps `TransportPublishRequest.Destination` equal to its configured `DefaultDestination`.

## Resolution Order

When an `ITenantRoutingStrategy` is registered, it has first priority.

Without a strategy, inbox dispatch resolves the route in this order:

1. `TransportInboxDispatcherOptions.ResolveRoute`.
2. `InboxEnvelope.ContractName`.

Without a strategy, outbox dispatch resolves the route in this order:

1. `OutboxEnvelope.Topic` when non-empty.
2. `TransportOutboxDispatcherOptions.ResolveRoute`.
3. `OutboxEnvelope.ContractName`.

For example, a strategy can route `tenant-east` order events to `tenant-east.orders.priority` while every publish still targets the configured `orders` destination.

## Registration

Register one `ITenantRoutingStrategy` in the application dependency container. `LiteBus.Inbox.Dispatch` and `LiteBus.Outbox.Dispatch` resolve it as an optional dependency. Broker-specific transport packages do not define tenant policy.

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

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Transport.Abstractions` | Declares `ITenantRoutingStrategy` |
| `LiteBus.Inbox.Dispatch` | Applies the strategy to inbox transport routes |
| `LiteBus.Outbox.Dispatch` | Applies the strategy to outbox transport routes |

## Invariants

- Tenant routing is optional.
- The strategy changes the route and cannot replace the configured transport destination.
- Tenant metadata can still propagate through `TransportHeaders.TenantId` without a routing strategy.
- Processor tenant leasing uses `InboxProcessorOptions.TenantId` or `OutboxProcessorOptions.TenantId`; it does not call the transport strategy.

## Non-Goals

- Tenant discovery from host context.
- Tenant authorization.
- Processor lease filtering.
- Per-tenant broker connection pools.

## Observability

Tenant identifiers are not metric tags because tenant counts can create unbounded cardinality. Inspect the selected transport route in adapter activities and logs. Use manager tenant filters for durable queue inspection.

## Test Coverage

- `TransportInboxDispatcherTests.DispatchAsync_WithTenantRoutingStrategy_ShouldUseResolvedRoute` verifies the inbox dispatcher passes tenant and contract metadata to the strategy and publishes the returned route.
- `TransportOutboxDispatcherTests.DispatchAsync_WithTenantRoutingStrategy_ShouldUseResolvedRoute` verifies the outbox dispatcher passes tenant, contract, and topic metadata and publishes the returned route.
- Broker dispatch integration tests verify canonical `tenant-id` header propagation independently of custom routing.

## Related Documentation

- [Tenant Scoping](../durable-core/tenant-scoping.md)
- [Canonical Headers](canonical-headers.md)
- [Transport Dispatch Core](../dispatch/transport-core.md)
