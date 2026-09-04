# Composite Parent and Child Modules

- **ID**: `runtime.composite-modules`
- **Name**: Composite parent and child modules
- **Maturity**: GA
- **Summary**: Allows parent modules to declare child modules during registration through `ICompositeModule`.

## What It Does

When `ModuleRegistry.Register` receives an `ICompositeModule`, it calls `DeclareChildren(Action<IModule>)` and registers each child immediately. This keeps parent-owned module graphs deterministic.

## Public Surface

| API | Role |
| --- | --- |
| `ICompositeModule` | Parent-child module contract |
| `DeclareChildren(Action<IModule>)` | Child declaration callback |

## Packages

- `LiteBus.Runtime.Abstractions`
- `LiteBus.Runtime`

## Requires

- `runtime.modules`
- `runtime.module-registry`

## Invariants

- Children are declared during registration, not build.
- Duplicate child type registration fails.

## Non-Goals

- Runtime dynamic child discovery.

## Observability

No direct telemetry. Duplicate paths fail fast at compose time.

## Test Coverage

### Covered Use Cases

#### `CompositeModuleRegistryTests.Register_CompositeModule_ShouldExpandChildrenImmediatelyAfterParent`
- **Test kind**: Unit
- **Expected outcome**: children inserted after parent

#### `CompositeModuleRegistryTests.Register_CompositeChildAlsoRegisteredAtTopLevel_ShouldThrowConfigurationException`
- **Test kind**: Unit
- **Expected outcome**: duplicate child path is rejected

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Deep nested composite chains | Low | Recursive path exists but not stress-tested |

### Out-of-Scope Use Cases

- Post-startup child module insertion.
