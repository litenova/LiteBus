# Azure Blob Checkpoint Anchor

`LiteBus.Auditing.Anchors.AzureBlob` anchors audit checkpoints in Azure Blob Storage. `AzureBlobCheckpointPublisher` writes one write-once blob per checkpoint identifier and then applies retention, and `AzureBlobCheckpointSource` reads back the highest-sequence checkpoint per segment as a verification comparison. Use it when checkpoints must survive the database they describe, under immutability policies or a legal hold.

For the checkpoint lifecycle and verification comparisons, see [Auditing](../concepts/auditing.md#checkpoints-and-publication).

## Install

Reference `LiteBus.Auditing` and `LiteBus.Auditing.Anchors.AzureBlob`.

## Register

```csharp
services.AddLiteBus(registry =>
{
    registry.AddAuditing(auditing => auditing
        .UseMartenStore()
        .UseHashChain(chain => chain
            .PublishCheckpointsToAzureBlob(anchor =>
            {
                anchor.ConnectionString = configuration.GetConnectionString("AuditAnchor")!;
                anchor.ContainerName = "audit-checkpoints";
                anchor.BlobNamePrefix = "orders/";
                anchor.ImmutabilityRetention = TimeSpan.FromDays(365);
            })));
});
```

`PublishCheckpointsToAzureBlob` records the anchor publisher and source types on the chain, so every cut checkpoint is published to Azure Blob Storage and verification compares against the stored copy, and registers the anchor module as a child of the auditing module carrying the settings, so the container constructs the publisher and source with their settings and client.

## Settings

| Setting | Role |
| --- | --- |
| `ConnectionString` | The Azure Storage connection string used to create the shared blob service client |
| `ContainerName` | The container holding checkpoint blobs. It is created with the first published checkpoint |
| `BlobNamePrefix` | The blob name prefix checkpoints are written under; empty writes at the container root |
| `ImmutabilityRetention` | How long an unlocked immutability policy protects each checkpoint blob after it is written; no policy by default |
| `LegalHold` | Whether every published blob carries a legal hold until it is explicitly cleared; `false` by default |

The container, its redundancy, and any container-level retention lock are deployment concerns owned by the application and its infrastructure. The package writes bytes only. The immutability policy is always unlocked, so its expiry can still be extended; a locked policy cannot be shortened or removed, which belongs to container-level compliance configuration rather than a per-checkpoint publisher. Azurite does not implement immutability policies or legal holds, so leave both unset in tests that run against it.

## Anchor Behavior

Each checkpoint blob holds JSON with every checkpoint field: identifier, segment, sequence, count, hash, scheme, kind, label, and signatures. Blobs are write-once per checkpoint id: a republish with identical bytes succeeds, and a republish with different bytes under the same identifier throws `AzureBlobCheckpointAnchorException` naming the checkpoint and the blob.

`GetLatestAsync` reads back the highest-sequence checkpoint per segment. An unreadable checkpoint blob fails the read instead of being skipped, while blobs without the checkpoint suffix are ignored as foreign files. Storage input and output failures propagate for the verifier to report as unreachable, separate from a source that disagrees, and a missing checkpoint reads as null.

The `auditing.anchor.azure_blob.connectivity` probe reports a missing container as degraded, because the publisher creates it with the first checkpoint, and an unreachable container as unhealthy.

## Related Docs

- [Auditing](../concepts/auditing.md)
- [S3 Checkpoint Anchor](auditing-s3-anchor.md)
- [RFC 3161 Timestamps](auditing-rfc3161-timestamp.md)
