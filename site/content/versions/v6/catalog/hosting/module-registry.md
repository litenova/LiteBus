# Module Registry and Build Order

## Header

- **ID**: `hosting.module-registry`
- **Name**: Module registry and build order
- **Maturity**: GA
- **Summary**: Registers modules, expands composite children, enforces dependency order, and freezes registration at build time.

## What It Does

`ModuleRegistry` stores module instances and computes dependency-safe build order with topological sort. It enforces duplicate detection, missing dependency checks, and circular dependency checks before any host build starts.

Composite modules are expanded during registration through `ICompositeModule.DeclareChildren(...)`. This keeps child modules in the same dependency graph and ordering pass.

## Public Surface

### Consumer Contracts

- `IModuleRegistry.Register(IModule module)`
- `IModuleRegistry.BuildOrder()`
- `IModuleRegistry.IsModuleRegistered<T>()`

### Behavior

- `BuildOrder()` freezes further registration.
- Dependencies come from `IRequires<TModule>` declarations.
- Missing dependencies and cycles throw `LiteBusConfigurationException`.

## Packages

- `LiteBus.Runtime`
- `LiteBus.Runtime.Abstractions`

## Requires

- `runtime.modules` (module contracts and dependency interfaces)

## Invariants

- A module type can be registered only once per composition.
- Dependencies are built before dependents.
- Registration after first `BuildOrder()` call is rejected.

## Non-Goals

- Dynamic module registration after host start.
- Runtime plugin loading.

## Observability

No dedicated metrics. Failures are surfaced as configuration exceptions during composition.

## Test Coverage

### Covered Use Cases

#### `ModuleRegistryTests.Register_DuplicateModuleType_ShouldThrowLiteBusConfigurationException`

- **Use case**: duplicate module registration guard
- **Test kind**: Unit
- **Description**: registers same module type twice
- **Behavior**: second registration is attempted
- **Expected outcome**: configuration exception with duplicate guidance
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleRegistryTests.cs`

#### `ModuleRegistryTests.Enumerate_WithDependencyChain_ShouldOrderDependenciesFirst`

- **Use case**: dependency ordering
- **Test kind**: Unit
- **Description**: registers chain A->B->C modules
- **Behavior**: computes `BuildOrder()`
- **Expected outcome**: C before B before A
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleRegistryTests.cs`

#### `ModuleRegistryTests.Enumerate_WithCircularDependency_ShouldThrowLiteBusConfigurationException`

- **Use case**: cycle detection
- **Test kind**: Unit
- **Description**: registers modules with circular requirements
- **Behavior**: computes `BuildOrder()`
- **Expected outcome**: configuration exception for circular dependency
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleRegistryTests.cs`

#### `ModuleRegistryTests.Enumerate_WithMissingRequiredModule_ShouldThrowLiteBusConfigurationException`

- **Use case**: missing dependency detection
- **Test kind**: Unit
- **Description**: registers module requiring unregistered dependency
- **Behavior**: computes `BuildOrder()`
- **Expected outcome**: configuration exception naming missing dependency
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleRegistryTests.cs`

#### `ModuleRegistryTests.Register_AfterBuildOrder_ShouldThrowLiteBusConfigurationException`

- **Use case**: freeze-after-build contract
- **Test kind**: Unit
- **Description**: builds order and then attempts new registration
- **Behavior**: calls `Register(...)` after freeze
- **Expected outcome**: configuration exception
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleRegistryTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Very large graph performance characteristics | Low | Current coverage focuses on correctness and errors. |

### Out-of-Scope Use Cases

- Runtime module hot swap.
- Automatic dependency injection of missing modules.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
- [Architecture](../../architecture/README.md)
