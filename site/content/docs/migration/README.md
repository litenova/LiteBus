# Migration

This page indexes version-to-version upgrade guides for LiteBus. Each guide lists breaking changes and the steps to move your code forward. Start with the guide for the version you are leaving.

> **This page is for** developers who upgrade LiteBus across a major version.

## Guides

| From to To | What changed | Guide |
| --- | --- | --- |
| v6.x to v7.0 | Decision stages with a fixed order (guards, validators, shortcuts, pre-handlers) plus a completion stage; validators return `Validity` instead of throwing; the synchronous handler layer and `Abort` are removed; the PostgreSQL outbox moves to schema version 2 with an automatic upgrade; Kafka, SQS, and AMQP ingress metadata change. | [Migration Guide v7](v7.md) |
| v5.x to v6.0 | Nested module builders, `AcceptAsync` / `EnqueueAsync`, pipelined processors, `net10.0` only, and new PostgreSQL version 1 schemas. | [Migration Guide v6](v6.md) |
| v4.x to v5.0 | The attribute-based command inbox is replaced by explicit inbox and outbox modules with stable contracts and role-split stores. Descriptor and open generic failures now throw typed exceptions. The solution moved to `LiteBus.slnx`. | [Migration Guide v5](v5.md) |
| v3.x to v4.0 | The library became DI-agnostic with container-specific extension packages. Event mediation gained `[HandlerPriority]` and concurrency controls. Several interfaces and properties were renamed. | [Migration Guide v4](v4.md) |

## Upgrading Across More Than One Major Version

Apply the guides in order, oldest first. A v3 application moving to v7 follows the v4 guide, then v5, then v6, then v7. Do not skip an intermediate guide: each later guide assumes the package and naming changes from earlier versions are already in place.

## Next

- New to LiteBus: [Getting Started](../getting-started/README.md) and the [Cheat Sheet](../getting-started/cheat-sheet.md).
- Upgrading from v6: [Migration Guide v7](v7.md).
- Upgrading from v5: [Migration Guide v6](v6.md).
- Coming from MediatR: [LiteBus vs. MediatR](mediatr-differences.md).
