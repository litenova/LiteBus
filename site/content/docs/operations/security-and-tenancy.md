# Security and Tenancy

**Production tier: GA**

LiteBus protects payloads at rest through application-supplied encryptors and scopes processor leases by tenant. The core libraries define the contracts; the application supplies key material and tenant values.

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Inbox` / `LiteBus.Outbox` | `UsePayloadEncryption`, `UseProcessorOptions` |
| `LiteBus.DurableMessaging.Abstractions` | `IPayloadEncryptor`, `IContextualPayloadEncryptor`, `TenantScope` |
| `LiteBus.Transport.Abstractions` | `ITenantRoutingStrategy` for publish routing |
| Your implementation | Encryptor plus key storage, rotation, and backup |

## Payload Encryption

Register an encryptor per axis. Inbox writers, transactional accept paths, and inbox dispatchers resolve `IInboxPayloadProtector`; the outbox resolves `IOutboxPayloadProtector`. The two keys are independent, so each axis can use different key material.

```csharp
public sealed class ApplicationPayloadEncryptor : IPayloadEncryptor
{
    private readonly byte[] _key;

    public ApplicationPayloadEncryptor(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _key = key;
    }

    public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Encrypt with your key material.");
    }

    public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Decrypt with your key material.");
    }
}
```

`ApplicationPayloadEncryptor` is a placeholder: implement `EncryptAsync` and `DecryptAsync` with your key vault and algorithm, then register it per axis.

```csharp
builder.AddInbox(inbox =>
{
    inbox.UsePayloadEncryption(new ApplicationPayloadEncryptor(key));
    // storage + dispatch...
});

builder.AddOutbox(outbox =>
{
    outbox.UsePayloadEncryption(new ApplicationPayloadEncryptor(key));
});
```

`IPayloadEncryptor` has two methods: `EncryptAsync(string plaintext, CancellationToken)` runs before a store write, and `DecryptAsync(string ciphertext, CancellationToken)` runs after a store read, before deserialization. Contract name and version stay plaintext in inbox and outbox tables so leasing, routing, and diagnostics keep working while payload bodies are protected at rest.

Implement `IContextualPayloadEncryptor` instead when ciphertext must not move between rows. The envelope factories build a `PayloadProtectionContext` for every write and pass it to the contextual overloads:

| Field | Value |
| --- | --- |
| `MessageId` | The durable message identifier |
| `ContractName`, `ContractVersion` | The stable contract stored beside the payload |
| `TenantId` | The tenant identifier stored beside the payload, when set |
| `Axis` | `inbox` or `outbox` |

Use the context as AEAD associated data so decryption fails when a ciphertext is copied to another message, contract, tenant, or axis. Existing `IPayloadEncryptor` implementations keep working: `PayloadProtection` calls the contextual overloads only when the registered encryptor implements `IContextualPayloadEncryptor`, and otherwise calls the plain methods.

## Key Rotation

Key rotation is an application responsibility. LiteBus stores ciphertext as opaque bytes; plan re-encryption or multi-key decrypt outside the framework. A common rotation keeps the previous key for reads while new writes use the next key, then re-encrypts stored rows during a maintenance window. Key loss is data loss: back up keys before rotation and document the restore procedure.

## Tenant Lease Isolation

Set `TenantId` on processor options to limit leasing to one tenant partition. The processor carries the value on every lease request, and the lease query filters by `tenant_id`.

```csharp
inbox.UseProcessorOptions(new InboxProcessorOptions { TenantId = "tenant-a" });
```

A `null` `TenantId` (the default) leases messages for all tenants. Multi-tenant hosts typically run one processor per tenant, each configured with its own `TenantId`, so tenants drain at their own pace and a poison message in one tenant never blocks another.

Envelopes carry the tenant on `TenantScope`: `Unscoped` for single-tenant applications, `Isolated` with an identifier for multi-tenant ones. Set it when the message is written:

```csharp
await inbox.AcceptAsync(
    InboxAcceptItem.WithTenant(command, "tenant-a"),
    cancellationToken).ConfigureAwait(false);

var metadata = OutboxEnqueueMetadata.Immediate with { Tenant = new TenantScope.Isolated("tenant-a") };
await outbox.EnqueueAsync(
    OutboxEnqueueItem<OrderPlacedEvent>.From(@event, metadata),
    cancellationToken).ConfigureAwait(false);
```

Validate the tenant at the API edge and audit the `tenant_id` column: isolation holds only when every envelope carries the right tenant and every processor filters on it. Stores normalize a missing tenant to the empty string for idempotency scopes and persisted rows (`DurableTenantId.Normalize`), so operator queries must treat null, whitespace, and empty tenant as the same unscoped value.

Tenant lease isolation is separate from publish routing. `ITenantRoutingStrategy.ResolveRoute(tenantId, contractName, topic)` computes the transport route for one envelope at dispatch time; configure it through `TransportOutboxDispatcherOptions.TenantRoutingStrategy` when tenants publish to different destinations.

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| Encrypted payloads stored as opaque bytes at rest | Key rotation or recovery without application key management |
| Tenant filter applied in lease SQL when `TenantId` is set on processor options | Cross-tenant isolation when an envelope or a lease request carries the wrong tenant |
| Contract names stay plaintext for routing and diagnostics | Payload confidentiality before `UsePayloadEncryption` is registered |

## Operations

| Risk | Mitigation |
| --- | --- |
| Key loss | Backup keys; document rotation and restore procedures |
| Wrong tenant on accept | Validate tenant at API edge; audit `tenant_id` column |
| Cleartext in logs | Do not log decrypted payloads; use correlation IDs |

## Tests

| Scenario | Location |
| --- | --- |
| Encrypted payload round-trip, dispatcher decryption, and rotated key rings | `LiteBus.Enterprise.UnitTests.PayloadEncryptionTests` |
| Inbox and outbox lease requests filtered by tenant id | `LiteBus.Enterprise.UnitTests.TenantLeaseFilterTests` |

## Related Docs

* [Inbox](../reliable-messaging/inbox.md), [Outbox](../reliable-messaging/outbox.md)
* [Production Runbook](runbook.md)
* [Management Endpoints](management-endpoints.md)
