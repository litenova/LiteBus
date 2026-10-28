# Module Registry and Build Order

- **ID**: `runtime.module-registry`
- **Name**: Module registry and build order
- **Maturity**: GA
- **Summary**: The registry registers modules, expands composites, computes the topological order, and freezes after `BuildOrder()`.

## What It Does

`ModuleRegistry` implements `IModuleRegistry`. It rejects duplicate module types, computes dependency order from `IRequires<TModule>`, and throws when dependencies are missing or circular.

Composite modules are expanded during registration through `ICompositeModule.DeclareChildren(...)`. This keeps child modules in the same dependency graph and ordering pass.

## Public Surface

| API | Role |
| --- | --- |
| `IModuleRegistry.Register(IModule)` | Registers module and composite children |
| `IModuleRegistry.IsModuleRegistered<T>()` | Exact type check |
| `IModuleRegistry.BuildOrder()` | Returns frozen dependency-ordered descriptors |

## Packages

- `LiteBus.Runtime`
- `LiteBus.Runtime.Abstractions`

## Requires

- `runtime.modules`
- `runtime.module-dependencies`

## Invariants

- Registering a duplicate module type throws.
- Registering after `BuildOrder()` throws.
- Composition fails on missing dependencies and dependency cycles.
- Dependencies are built before dependents.

## Non-Goals

- Translating registrations for DI adapters.
- Registering modules dynamically after the host starts.
- Loading plugins at run time.

## Observability

The registry emits no telemetry. Compose-time exceptions include explicit dependency diagnostics.

## Test Coverage

### Covered Use Cases

#### `ModuleRegistryTests.Register_DuplicateModuleType_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Expected outcome**: the registry rejects the duplicate module type

#### `ModuleRegistryTests.Enumerate_WithDependencyChain_ShouldOrderDependenciesFirst`
- **Test kind**: Unit
- **Expected outcome**: dependencies build before their dependents

#### `ModuleRegistryTests.Enumerate_WithCircularDependency_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Expected outcome**: the registry throws the cycle detection exception

#### `ModuleRegistryTests.Enumerate_WithMissingRequiredModule_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Description**: The test registers a module that requires an unregistered dependency
- **Expected outcome**: the registry throws a configuration exception that names the missing dependency
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleRegistryTests.cs`

#### `ModuleRegistryTests.Register_AfterBuildOrder_ShouldThrowLiteBusConfigurationException`
- **Test kind**: Unit
- **Description**: The test builds the order and then attempts a new registration
- **Expected outcome**: the registry throws a configuration exception
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/ModuleRegistryTests.cs`

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| High-scale graph ordering benchmarks | Low | Functional ordering is covered |

### Out-of-Scope Use Cases

- Mutating the graph at run time after it freezes.
- Injecting missing modules automatically.

## Further Reading

- [Hosted services](../../architecture/hosted-services.md)
- [Architecture](../../architecture/README.md)
