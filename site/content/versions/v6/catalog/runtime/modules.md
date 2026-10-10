# Module Contract

- **ID**: `runtime.modules`
- **Name**: Module contract
- **Maturity**: GA
- **Summary**: Defines compose-time module behavior through `IModule` and parent-child expansion through `ICompositeModule`.

## What It Does

Runtime modules are the composition unit for LiteBus. Each module implements `IModule.Build(IModuleConfiguration)` and contributes dependency registrations, context, and optional manifest entries.

## Public Surface

| API | Role |
| --- | --- |
| `IModule.Build(IModuleConfiguration)` | Compose-time callback |
| `ICompositeModule.DeclareChildren(Action<IModule>)` | Child declaration hook during registration |

## Packages

- `LiteBus.Runtime.Abstractions`

## Requires

- `runtime.module-registry`
- `runtime.module-configuration`

## Invariants

- `Build` runs after ordered module graph creation.
- Composite children are declared during registration, not build.

## Non-Goals

- Runtime host orchestration.

## Observability

No dedicated metrics. Registration failures surface as configuration exceptions.

## Test Coverage

### Covered Use Cases

#### `CompositeModuleRegistryTests.Register_CompositeModule_ShouldExpandChildrenImmediatelyAfterParent`
- **Use case**: parent expands child modules
- **Test kind**: Unit
- **Expected outcome**: children are registered immediately after parent

#### `ModuleRegistryTests.Register_WithNullModule_ShouldThrowArgumentNullException`
- **Use case**: invalid module registration input
- **Test kind**: Unit
- **Expected outcome**: null module is rejected

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Very large composite trees | Medium | No stress-focused test |

### Out-of-Scope Use Cases

- Runtime module hot-reload.
