# Security and Tenancy

**Production tier: GA**

LiteBus v6 supports optional payload encryption and tenant-scoped lease filters on inbox and outbox stores. These are application-supplied implementations; the core libraries define contracts only.

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Inbox` / `LiteBus.Outbox` | `UsePayloadEncryption`, tenant filter hooks |
| `LiteBus.Messaging.Abstractions` | `IPayloadEncryptor` |
| Your implementation | AES, envelope encryption, or KMS wrapper |

Enterprise unit tests demonstrate round-trip encryption and tenant lease scoping: `LiteBus.Enterprise.UnitTests`.

## Registration

### Payload Encryption

```csharp
builder.Modules.AddInboxModule(inbox =>
{
    inbox.UsePayloadEncryption(new AesPayloadEncryptor(key));
    // storage + dispatch...
});

builder.Modules.AddOutboxModule(outbox =>
{
    outbox.UsePayloadEncryption(new AesPayloadEncryptor(key));
});
```

Dispatchers resolve axis-specific protectors (`IInboxPayloadProtector`, `IOutboxPayloadProtector`) registered from your `IPayloadEncryptor`.

### Tenant Lease Filter

Configure store options so lease SQL filters by `tenant_id` when the request carries a tenant:

```csharp
inbox.UsePostgreSqlStorage(connectionString, options =>
{
    options.TenantId = "tenant-a";
});
```

Multi-tenant hosts typically register one store options factory per tenant or pass tenant on lease requests from processor options.

## Options Reference

| Contract | Role |
| --- | --- |
| `IPayloadEncryptor` | Encrypt/decrypt persisted payload bytes |
| `ITenantRoutingStrategy` | Optional routing metadata on publish (transport adapters) |
| Store `TenantId` on lease requests | Limits candidate rows in PostgreSQL/EF lease SQL |

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| Encrypted payloads stored as opaque bytes at rest | Key rotation without application migration |
| Tenant filter applied in lease SQL when configured | Cross-tenant isolation without correct tenant on every request |

## Operations

| Risk | Mitigation |
| --- | --- |
| Key loss | Backup keys; document rotation procedure |
| Wrong tenant on accept | Validate tenant at API edge; audit `tenant_id` column |
| Cleartext in logs | Do not log decrypted payloads; use correlation IDs |

## Tests

| Scenario | Location |
| --- | --- |
| Encryption round-trip | `LiteBus.Enterprise.UnitTests.PayloadEncryptionTests` |
| Tenant lease scoping | `LiteBus.Enterprise.UnitTests.TenantLeaseFilterTests` |

## Related Docs

* [Inbox](../reliable-messaging/inbox.md), [Outbox](../reliable-messaging/outbox.md)
* [Operations and management](README.md)
