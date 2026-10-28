# Payload Encryption Hook

- **ID**: `runtime.payload-protection`
- **Name**: Payload encryption hook
- **Maturity**: GA
- **Summary**: This capability adds optional payload text encryption and decryption through `IPayloadEncryptor` and `PayloadProtection`.

## What It Does

Runtime calls:

- `PayloadProtection.ProtectAsync(payload, encryptor, token)` before durable write
- `PayloadProtection.UnprotectAsync(payload, encryptor, token)` before deserialization

When no encryptor is configured, the payload passes through unchanged.

Overloads that take a `PayloadProtectionContext` pass durable metadata as associated data when the encryptor implements `IContextualPayloadEncryptor`. Any other encryptor, or a null context, uses the plain contract. The context carries the message id, contract name and version, tenant id, and durable axis, so ciphertext cannot move between rows, contracts, tenants, or axes without failing authentication.

## Public Surface

| API | Role |
| --- | --- |
| `IPayloadEncryptor.EncryptAsync(...)` | Encrypts payload |
| `IPayloadEncryptor.DecryptAsync(...)` | Decrypts payload |
| `PayloadProtection.ProtectAsync(...)` | Encrypt-or-pass-through helper |
| `PayloadProtection.UnprotectAsync(...)` | Decrypt-or-pass-through helper |
| `IContextualPayloadEncryptor.EncryptAsync(...)` / `DecryptAsync(...)` | Encrypts and decrypts while authenticating durable metadata |
| `PayloadProtectionContext` | Immutable metadata bound to ciphertext: message id, contract, tenant, axis |

## Packages

- `LiteBus.DurableMessaging.Abstractions`
- `LiteBus.Messaging`

## Requires

- `runtime.message-serialization`

## Invariants

- Null payload is rejected.
- With a null encryptor, the helpers return the original payload.
- Contract metadata remains plaintext.
- A null context, or an encryptor without contextual support, falls back to the plain encryptor contract.
- Unscoped tenant identifiers normalize to null in protection contexts, so null, empty, and whitespace authenticate identically.

## Non-Goals

- Workflows for key management and rotation.

## Observability

The runtime core adds no instrumentation for payload protection.

## Test Coverage

### Covered Use Cases

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`
- **Test kind**: Component
- **Expected outcome**: runtime durable flow keeps correlated payload lifecycle stable

#### `PayloadProtectionTests`

- **Use case**: Optional protection passes plaintext through or delegates to the configured encryptor.
- **Test kind**: Unit
- **Description**: The test invokes protect and unprotect paths directly with no encryptor, a recording encryptor, null payloads, and provider failures.
- **Behavior**: Payload and cancellation tokens are forwarded unchanged. Encryptor exceptions retain their provider type.
- **Expected outcome**: Pass-through and delegation behavior match the runtime boundary contract.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Serialization/`

### Out-of-Scope Use Cases

- An encryptor implementation built into the runtime.
