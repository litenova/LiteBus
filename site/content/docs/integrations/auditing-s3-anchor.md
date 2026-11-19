# S3 Checkpoint Anchor

`LiteBus.Auditing.Anchors.S3` anchors audit checkpoints in S3-compatible object storage. `S3CheckpointPublisher` writes one retained object per checkpoint identifier, and `S3CheckpointSource` reads back the highest-sequence checkpoint per segment as a verification comparison. Use it when checkpoints must survive the database they describe, under Object Lock retention.

For the checkpoint lifecycle and verification comparisons, see [Auditing](../concepts/auditing.md#checkpoints-and-publication).

## Install

Reference `LiteBus.Auditing` and `LiteBus.Auditing.Anchors.S3`.

## Register

```csharp
services.AddLiteBus(registry =>
{
    registry.AddAuditing(auditing => auditing
        .UseMartenStore()
        .UseHashChain(chain => chain
            .PublishCheckpointsToS3(anchor =>
            {
                anchor.BucketName = "audit-checkpoints";
                anchor.KeyPrefix = "orders/";
                anchor.Region = "us-east-1";
                anchor.RetentionMode = S3CheckpointRetentionMode.Governance;
                anchor.RetentionPeriod = TimeSpan.FromDays(365);
            })));
});
```

`PublishCheckpointsToS3` records the anchor publisher and source types on the chain, so every cut checkpoint is published to S3 and verification compares against the stored copy, and registers the anchor module as a child of the auditing module carrying the settings, so the container constructs the publisher and source with their settings and client.

## Settings

| Setting | Role |
| --- | --- |
| `BucketName` | The bucket holding checkpoint objects. It is created with the first published checkpoint |
| `KeyPrefix` | The object key prefix checkpoints are written under; empty writes at the bucket root |
| `Region` | The AWS region system name, such as `us-east-1`. When both region and service URL are unset, the AWS SDK resolves the region from the environment |
| `ServiceUrl` | The service URL for S3-compatible stores such as MinIO. Takes precedence over the region; an `http` URL selects plain HTTP automatically |
| `AccessKey`, `SecretKey` | Explicit credentials, when required |
| `ForcePathStyle` | Path-style addressing, which most S3-compatible stores require; virtual-hosted style by default |
| `RetentionMode`, `RetentionPeriod` | The Object Lock retention mode (`Governance` or `Compliance`) and how long it protects each checkpoint object after it is written; no retention by default |

The bucket, its region, and any bucket-level retention lock are deployment concerns owned by the application and its infrastructure. Buckets the package creates carry Object Lock when a retention mode is configured; a bucket provisioned without it rejects retained writes. The package writes bytes only and provisions no retention policy beyond the per-object retention above.

## Anchor Behavior

Each checkpoint is stored as C2SP transparency-log checkpoint text, so witnesses, timestamping authorities, and generic verifiers consume anchors without LiteBus code. The object key carries the segment, the sequence, and the checkpoint identifier with the checkpoint suffix. Identical republish writes the same deterministic bytes to the same key and succeeds; a checkpoint id is unique per cut, so a different checkpoint under a stored id cannot occur in normal operation.

`GetLatestAsync` lists the escaped segment prefix, parses every checkpoint object, and returns the highest sequence with object-key tie-break. Objects without the checkpoint suffix are skipped as foreign files. An unreadable anchor object throws `S3CheckpointAnchorException`; a missing bucket or an empty segment reads as null. Storage input and output failures propagate for the verifier to report as unreachable, separate from a source that disagrees.

The `auditing.anchor.s3.connectivity` probe reports a missing bucket as degraded, because the publisher creates it with the first checkpoint, and an unreachable bucket as unhealthy.

## Limitations

Object Lock versioning preserves the original bytes, so verification fails loudly against a replaced object rather than reading the replacement. There is no legal-hold setting: retention mode and period are the only retention controls.

## Related Docs

- [Auditing](../concepts/auditing.md)
- [Azure Blob Checkpoint Anchor](auditing-azure-blob-anchor.md)
- [RFC 3161 Timestamps](auditing-rfc3161-timestamp.md)
