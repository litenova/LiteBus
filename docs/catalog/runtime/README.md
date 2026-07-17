# Runtime Capability Catalog

LiteBus runtime capabilities define the compose-time module graph and the in-process messaging engine used by mediator, durable, transport, and hosting axes. This axis sits in layers 0 through 2 and stays container-neutral and host-neutral by design.

## Axis Scope

- Module composition contracts (`IModule`, `IModuleRegistry`, `IModuleConfiguration`)
- Dependency registration abstraction (`IDependencyRegistry`)
- Shared messaging foundations (`MessageModule`, message and contract registries)
- Core mediation engine (descriptor resolution, strategy execution, scoped dispatch)
- Durable metadata mapping and payload handling primitives (serialization, trace metadata, payload encryption hook)

## Packages

| Package | Layer | Role |
| --- | --- | --- |
| `LiteBus.Runtime.Abstractions` | 0 | Module, registry, dependency, and runtime exception contracts |
| `LiteBus.Runtime` | 2 | Default module registry, module configuration, dependency registry, builder |
| `LiteBus.Messaging.Abstractions` | 1 | Messaging contracts, descriptors, mediation requests, durable metadata value objects |
| `LiteBus.Messaging` | 2 | Message module, mediator engine, registries, serializer, metadata mapper |

## Capabilities (18)

| ID | Name | Maturity | Package(s) |
| --- | --- | --- | --- |
| [runtime.modules](modules.md) | Module contract | GA | `LiteBus.Runtime.Abstractions` |
| [runtime.module-registry](module-registry.md) | Module registry and build order | GA | `LiteBus.Runtime` |
| [runtime.module-dependencies](module-dependencies.md) | Module dependency ordering | GA | `LiteBus.Runtime`, `LiteBus.Runtime.Abstractions` |
| [runtime.module-configuration](module-configuration.md) | Module configuration and shared context | GA | `LiteBus.Runtime` |
| [runtime.composite-modules](composite-modules.md) | Composite parent and child modules | GA | `LiteBus.Runtime`, `LiteBus.Runtime.Abstractions` |
| [runtime.litebus-builder](litebus-builder.md) | Composition builder surface | GA | `LiteBus.Runtime` |
| [runtime.dependency-registry](dependency-registry.md) | Container-neutral dependency registry | GA | `LiteBus.Runtime` |
| [runtime.message-module](message-module.md) | Foundational messaging module | GA | `LiteBus.Messaging` |
| [runtime.message-registry](message-registry.md) | Message and handler type registry | GA | `LiteBus.Messaging`, `LiteBus.Messaging.Abstractions` |
| [runtime.contract-registry](contract-registry.md) | Message contract registry | GA | `LiteBus.Messaging`, `LiteBus.Messaging.Abstractions` |
| [runtime.message-mediator](message-mediator.md) | Core message mediator | GA | `LiteBus.Messaging` |
| [runtime.mediation-strategies](mediation-strategies.md) | Pluggable mediation strategies | GA | `LiteBus.Messaging`, `LiteBus.Messaging.Abstractions` |
| [runtime.handler-descriptors](handler-descriptors.md) | Handler descriptor model | GA | `LiteBus.Messaging.Abstractions` |
| [runtime.message-resolution](message-resolution.md) | Message resolve strategies | GA | `LiteBus.Messaging` |
| [runtime.dispatch-scopes](dispatch-scopes.md) | Per-mediation DI scopes | GA | `LiteBus.Messaging` |
| [runtime.message-serialization](message-serialization.md) | Message serialization | GA | `LiteBus.Messaging` |
| [runtime.payload-protection](payload-protection.md) | Payload encryption hook | GA | `LiteBus.Messaging`, `LiteBus.Messaging.Abstractions` |
| [runtime.trace-metadata](trace-metadata.md) | Trace metadata and propagation | GA | `LiteBus.Messaging.Abstractions`, `LiteBus.Messaging` |

## Runtime and Messaging Flow

```text
AddLiteBus(...)
  -> modules register in IModuleRegistry
  -> BuildOrder() computes topological order and freezes registry
  -> each module Build(IModuleConfiguration) runs in order
  -> MessageModule creates IMessageRegistry and IMessageContractRegistry contexts
  -> mediator resolves message descriptor and handlers from scoped provider
  -> strategy executes pre/main/post/error pipeline
```

## Test Sources

| Area | Primary test projects |
| --- | --- |
| Module contracts, registry ordering, dependency registry, builder, module configuration | `LiteBus.Runtime.UnitTests` |
| Messaging registry, mediator behavior, resolution strategy, open generics, context propagation | `LiteBus.Mediator.UnitTests` |

## Cross-Axis Links

- Runtime module ordering in this axis: [runtime.module-registry](module-registry.md)
- Host manifest bridge of the same ordering output: [hosting.module-registry](../hosting/module-registry.md)
- Architecture context: [Architecture](../../architecture/README.md)
- Extensibility guidance: [Extensibility](../../extending/README.md)
