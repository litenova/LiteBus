# RFC 3161 Timestamps

`LiteBus.Auditing.Anchors.Timestamping` signs audit checkpoints with time-stamp tokens from an RFC 3161 time-stamping authority. `Rfc3161CheckpointSigner` posts the checkpoint hash to the authority and returns the issued token as one more checkpoint signature, so independent time proof sits beside the log signature and witness cosignatures. Use it when a checkpoint must prove not only what the chain held but when an independent authority saw it.

For the checkpoint lifecycle and the signature list, see [Auditing](../concepts/auditing.md#checkpoints-and-publication).

## Install

Reference `LiteBus.Auditing` and `LiteBus.Auditing.Anchors.Timestamping`.

## Register

```csharp
services.AddLiteBus(registry => registry.AddAuditing(auditing => auditing
    .UseMartenStore()
    .UseHashChain(chain => chain
        .TimestampCheckpoints(new Uri("https://tsa.example.com/tsa")))));
```

`TimestampCheckpoints` registers `Rfc3161CheckpointSigner`, which reads the authority from `Rfc3161TimestampOptions`. The authority must be an absolute HTTP or HTTPS URI; anything else fails composition. Registering twice adds the signer once.

## Settings

`Rfc3161TimestampOptions` carries the authority and the token settings:

| Setting | Role |
| --- | --- |
| `Authority` | The time-stamping authority timestamp queries are posted to. The only required value |
| `HashAlgorithm` | The hash algorithm naming the checkpoint digest inside timestamp requests. Defaults to SHA-256 and must stay the same between signing and verification |
| `RequestSignerCertificates` | Whether the authority must include its signing certificate in issued tokens. `true` unless the authority policy forbids it; verification then needs the authority certificate in the pinned list |
| `Timeout` | How long a timestamp request waits for the authority. Defaults to 30 seconds and must be positive |
| `TrustedSignerCertificates` | The authority certificates verification trusts, compared byte for byte against the token signer. Empty by default, which accepts any signer whose signature is valid |

## Signer Behavior

Signing posts an `Rfc3161TimestampRequest` for the checkpoint hash bytes with a random nonce to the configured authority, and returns the issued token as an `AuditCheckpointSignature` under the `rfc3161` scheme. The signature value is the Base64 encoding of the DER-encoded token. The checkpoint lifecycle collects it with the other signatures. A signing failure throws `Rfc3161TimestampException` with the checkpoint context and the transport or protocol cause, so a lost timestamp surfaces instead of passing silently.

Verification decodes the stored token and checks that it covers the checkpoint hash, with no network call. When pinned certificates are configured, the token signer must match one byte for byte; otherwise any valid signature is accepted. Pinning the authority certificate makes verification report a token signed by anyone else as invalid. Chain building and trust policy stay with the application, because LiteBus does not own timestamping authorities.

An application that needs proxying, authentication headers, or handler lifetime tuning passes its own `HttpClient` to the signer; otherwise the signer keeps one client for its own lifetime. The signer is thread-safe: the options are immutable once constructed, and the client is shared across calls.

## Related Docs

- [Auditing](../concepts/auditing.md)
- [S3 Checkpoint Anchor](auditing-s3-anchor.md)
- [Azure Blob Checkpoint Anchor](auditing-azure-blob-anchor.md)
