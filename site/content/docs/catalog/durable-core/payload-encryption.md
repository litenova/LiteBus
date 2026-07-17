# Payload Encryption at Rest

**ID:** `durable-core.payload-encryption`
**Maturity:** GA

## Summary

Inbox and outbox writers can encrypt serialized payload bodies before persistence. Dispatchers decrypt after load and before deserialization. Contract names, versions, routing fields, and operator metadata remain readable.

## Public Surface

| Surface | Role |
| --- | --- |
| `IPayloadEncryptor` | Application implementation of `EncryptAsync` and `DecryptAsync` |
| `IInboxPayloadProtector` | Inbox-specific dependency key adapted from the configured encryptor |
| `IOutboxPayloadProtector` | Outbox-specific dependency key adapted from the configured encryptor |
| `InboxModuleBuilder.UsePayloadEncryption` | Applies protection to inbox acceptance and dispatch |
| `OutboxModuleBuilder.UsePayloadEncryption` | Applies protection to outbox enqueue and dispatch |

Register protection on each durable axis that shares the encrypted store:

```csharp
var encryptor = new ApplicationPayloadEncryptor(keyProvider);

registry.AddInbox(inbox =>
{
    inbox.UsePayloadEncryption(encryptor);
});

registry.AddOutbox(outbox =>
{
    outbox.UsePayloadEncryption(encryptor);
});
```

Inbox and outbox can use separate implementations when they use different keys or providers.

## Runtime Boundaries

| Component | Protection Operation |
| --- | --- |
| `InboxEnvelopeFactory` | Encrypts serialized commands before store insertion |
| `OutboxEnvelopeFactory` | Encrypts serialized events before store insertion |
| Transactional EF Core inbox and outbox writers | Encrypt within the caller's unit of work |
| In-process and transport dispatchers | Decrypt stored payloads before validation or mediation |

Storage adapters treat the protected payload as opaque text. Contract name and version stay plaintext so the registry can select a CLR type before deserialization. Topic, correlation, tenant, visibility, and status fields also remain plaintext for leasing and operations.

## Key Rotation

LiteBus does not own keys or rotation schedules. The application encryptor can implement online rotation with a key identifier in its ciphertext format:

1. Keep historical read keys available.
2. Change the active write key.
3. Write new payloads with the new key identifier.
4. Select the matching read key from each stored ciphertext.
5. Retire a historical key only after no retained row depends on it.

For example, rows prefixed with `key-v1:` and `key-v2:` can coexist while `DecryptAsync` selects from an application key ring. New writes use `key-v2`; old rows remain dispatchable through `key-v1`.

The test key ring in `PayloadEncryptionTests` models identifier selection and mixed historical reads. It is not a cryptographic algorithm reference. Applications should use an authenticated encryption scheme and managed key material suitable for their security requirements.

## Failure Behavior

- An encryption failure aborts accept or enqueue before the store write completes.
- A decryption failure is a dispatch failure and follows processor retry and dead-letter policy.
- Removing a historical read key while retained rows still reference it makes those rows undispatchable.
- Changing the ciphertext format requires a reader that recognizes every retained format version.

Repeated decrypt failures after a deployment usually identify missing keys, mismatched configuration, or an incompatible ciphertext format. The processor failure and dead-letter counters expose the durable impact; the encryptor should log a non-secret key identifier and failure category.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.DurableMessaging.Abstractions` | Shared `IPayloadEncryptor` contract |
| `LiteBus.Inbox.Abstractions` | Inbox protector contract |
| `LiteBus.Outbox.Abstractions` | Outbox protector contract |
| `LiteBus.Inbox` | Inbox builder surface and encryption/decryption integration |
| `LiteBus.Outbox` | Outbox builder surface and encryption/decryption integration |
| Application | Algorithm, key provider, key identifiers, rotation, and audit policy |

## Invariants

- Protection is optional; the default stores serialized JSON as plaintext.
- Encryption runs before the payload reaches the store.
- Decryption runs before payload validation or deserialization.
- All readers of one store must retain keys for every non-expired ciphertext.
- Contract and operational metadata are not encrypted.
- Transactional writers use the same axis protector as non-transactional writers.

## Non-Goals

- Built-in KMS, HSM, or key-vault integration.
- Automatic key generation, rotation, re-encryption, or retirement.
- Field-level payload encryption.
- Encryption of contract, routing, lease, or status columns.

## Observability

LiteBus does not emit key identifiers as metric tags because key and tenant sets can create high cardinality. Use these signals:

| Signal | Meaning |
| --- | --- |
| Writer exception | Encryption failed before durable insertion |
| `litebus.inbox.processor.failed` | Inbox decrypt, deserialize, hook, or handler failure scheduled a retry |
| `litebus.outbox.processor.failed` | Outbox decrypt, validate, or dispatch failure scheduled a retry |
| Dead-letter counters | Retry policy exhausted, including persistent decrypt failures |
| Dispatch duration histogram | Includes decrypt time inside the dispatch operation |

## Test Coverage

- `PayloadEncryptionTests.Inbox_RoundTripsEncryptedPayloadThroughDispatch` verifies ciphertext at rest and plaintext delivery.
- `PayloadEncryptionTests.CommandInboxDispatcher_DecryptsProtectedPayload` and `EventOutboxDispatcher_DecryptsProtectedPayload` verify both in-process dispatch paths.
- `PayloadEncryptionTests.Inbox_WithRotatedKeyRing_ShouldDispatchHistoricalAndCurrentCiphertext` writes one row with `key-v1`, rotates the active key to `key-v2`, writes another row, and dispatches both through one retained key ring.
- `InboxEnvelopeFactoryTests.CreateAsync_should_encrypt_payload_when_protector_configured` verifies the inbox writer boundary.
- `TransactionalInboxAcceptTests` and `TransactionalOutboxEnqueueTests` verify encryption inside EF Core transactional staging.

PostgreSQL stores use the same envelope factories and opaque payload contract. Store-specific encryption does not exist.

## Related Documentation

- [Security and Tenancy](../../operations/security-and-tenancy.md)
- [Inbox](../../reliable-messaging/inbox.md)
- [Outbox](../../reliable-messaging/outbox.md)
- [Architecture](../../architecture/README.md)
