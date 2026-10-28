# Migration

LiteBus ships one upgrade guide per major version. Each guide lists the breaking changes in that release and the steps that upgrade your code. This page indexes those guides. Start with the guide for the version you are leaving.

> **This page is for** developers who upgrade LiteBus across a major version.

## Guides

| Upgrade Path | Changes | Guide |
| --- | --- | --- |
| v6.x to v7.0 | Decision stages run in a fixed order (guards, validators, shortcuts, pre-handlers), followed by a completion stage. Validators return `Validity` instead of throwing. The synchronous handler layer and `Abort` are removed. The PostgreSQL outbox moves to schema version 2 with an automatic upgrade. Kafka, SQS, and AMQP ingress metadata change. | [Migration Guide v7](v7.md) |
| v5.x to v6.0 | Version 6 adds nested module builders, `AcceptAsync` / `EnqueueAsync` writer calls, and pipelined processors, targets `net10.0` only, and ships new PostgreSQL version 1 schemas. | [Migration Guide v6](v6.md) |
| v4.x to v5.0 | Explicit inbox and outbox modules with stable contracts and role-split stores replace the attribute-based command inbox. Descriptor and open generic failures now throw typed exceptions. The solution moved to `LiteBus.slnx`. | [Migration Guide v5](v5.md) |
| v3.x to v4.0 | Container-specific extension packages replace the single DI integration. Event mediation adds `[HandlerPriority]` and concurrency controls. Interfaces and properties were renamed. | [Migration Guide v4](v4.md) |

## Upgrading Across More than One Major Version

Apply the guides in order, oldest first. A v3 application moving to v7 follows the v4 guide, then v5, then v6, then v7. Do not skip an intermediate guide. Each later guide assumes the package and naming changes from earlier versions are already in place.

## Next

- New to LiteBus: [Getting Started](../getting-started/README.md) and the [Cheat Sheet](../getting-started/cheat-sheet.md).
- Upgrading from v6: [Migration Guide v7](v7.md).
- Upgrading from v5: [Migration Guide v6](v6.md).
- Coming from MediatR: [LiteBus vs. MediatR](mediatr-differences.md).
