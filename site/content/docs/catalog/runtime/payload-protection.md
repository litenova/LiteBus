# Payload Encryption Hook

- **ID**: `runtime.payload-protection`
- **Name**: Payload encryption hook
- **Maturity**: GA
- **Summary**: Adds optional payload text encryption and decryption through `IPayloadEncryptor` and `PayloadProtection`.

## What It Does

Runtime calls:

- `PayloadProtection.ProtectAsync(payload, encryptor, token)` before durable write
- `PayloadProtection.UnprotectAsync(payload, encryptor, token)` before deserialization

With no encryptor, payload is passed through unchanged.

## Public Surface

| API | Role |
| --- | --- |
| `IPayloadEncryptor.EncryptAsync(...)` | Encrypts payload |
| `IPayloadEncryptor.DecryptAsync(...)` | Decrypts payload |
| `PayloadProtection.ProtectAsync(...)` | Encrypt-or-pass-through helper |
| `PayloadProtection.UnprotectAsync(...)` | Decrypt-or-pass-through helper |

## Packages

- `LiteBus.Messaging.Abstractions`
- `LiteBus.Messaging`

## Requires

- `runtime.message-serialization`

## Invariants

- Null payload is rejected.
- Null encryptor returns original payload.
- Contract metadata remains plaintext.

## Non-Goals

- Key management and rotation workflows.

## Observability

No dedicated instrumentation in runtime core.

## Test Coverage

### Covered Use Cases

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`
- **Test kind**: Component
- **Expected outcome**: runtime durable flow keeps correlated payload lifecycle stable

#### `PayloadProtectionTests`

- **Use case**: Optional protection passes plaintext through or delegates to the configured encryptor.
- **Test kind**: Unit
- **Description**: Directly invokes protect and unprotect paths with no encryptor, a recording encryptor, null payloads, and provider failures.
- **Behavior**: Payload and cancellation tokens are forwarded unchanged; encryptor exceptions retain their provider type.
- **Expected outcome**: Pass-through and delegation behavior match the runtime boundary contract.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Serialization/`.

### Out-of-Scope Use Cases

- Built-in encryptor implementation.
