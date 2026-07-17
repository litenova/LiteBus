# Migration Guides

This page indexes version-to-version upgrade guides for LiteBus. Each guide lists breaking changes and the steps to move your code forward. Start with the guide for the version you are leaving.

## Guides

| From to To | What changed | Guide |
| --- | --- | --- |
| v5.x to v6.0 | Greenfield durable messaging: nested module builders, `AcceptAsync` / `EnqueueAsync`, pipelined processors, and `net10.0` only. Current v6 PostgreSQL schemas are inbox/outbox v3 and saga v2. | [Migration Guide v6](v6.md) |
| v4.x to v5.0 | The attribute-based command inbox is replaced by explicit inbox and outbox modules with stable contracts and role-split stores. Descriptor and open generic failures now throw typed exceptions. The solution moved to `LiteBus.slnx`. | [Migration Guide v5](v5.md) |
| v3.x to v4.0 | The library became DI-agnostic with container-specific extension packages. Event mediation gained `[HandlerPriority]` and concurrency controls. Several interfaces and properties were renamed. | [Migration Guide v4](v4.md) |

## Upgrading Across More Than One Major Version

Apply the guides in order, oldest first. A v3 application moving to v6 follows the v4 guide, then the v5 guide, then the v6 guide. Do not skip an intermediate guide: each later guide assumes the package and naming changes from earlier versions are already in place.

## Next

- New to LiteBus: [Getting Started](../getting-started/README.md) and the [Cheat Sheet](../getting-started/cheat-sheet.md).
- Upgrading from v5: [Migration Guide v6](v6.md).
- Coming from MediatR: [Migrating from MediatR](from-mediatr.md).
