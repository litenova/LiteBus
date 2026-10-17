# Composite Parent and Child Modules

- **ID**: `runtime.composite-modules`
- **Name**: Composite parent and child modules
- **Maturity**: GA
- **Summary**: Allows parent modules to declare child modules during registration through `ICompositeModule`.

## What It Does

When `IModuleRegistry.Register` receives an `ICompositeModule`, it calls `DeclareChildren(Action<IModule>)` and stages each child immediately after the parent in the same registration call. This keeps parent-owned module graphs deterministic.

## Public Surface

| API | Role |
| --- | --- |
| `ICompositeModule` | Parent-child module contract |
| `DeclareChildren(Action<IModule>)` | Child declaration callback |
| `ICompositeModule.BuildOrder` (`CompositeModuleBuildOrder`) | Parent-first by default; children-first reverses the ordering edge |

## Packages

- `LiteBus.Runtime.Abstractions`
- `LiteBus.Runtime`

## Requires

- `runtime.modules`
- `runtime.module-registry`

## Invariants

- Children are declared during registration, not build.
- Duplicate child type registration fails.
- Parent-first build order is the default; children-first adds the reversed ordering edge before topological sort.
- A failed composite expansion commits nothing to the registry.

## Non-Goals

- Runtime dynamic child discovery.

## Observability

No direct telemetry. Duplicate registrations fail fast at compose time.

## Test Coverage

### Covered Use Cases

#### `CompositeModuleRegistryTests.Register_CompositeModule_ShouldExpandChildrenImmediatelyAfterParent`
- **Test kind**: Unit
- **Expected outcome**: children inserted after parent

#### `CompositeModuleRegistryTests.Register_CompositeChildAlsoRegisteredAtTopLevel_ShouldThrowConfigurationException`
- **Test kind**: Unit
- **Expected outcome**: duplicate child path is rejected

#### `CompositeModuleRegistryTests.BuildOrder_WithChildrenFirstComposite_ShouldBuildChildBeforeParent`
- **Test kind**: Unit
- **Expected outcome**: children-first edge builds the child before the parent

#### `CompositeModuleRegistryTests.BuildOrder_WithNestedOppositeRelationships_ShouldHonorEveryCompositeEdge`
- **Test kind**: Unit
- **Expected outcome**: nested composite ordering edges are honored

#### `CompositeModuleRegistryTests.Register_WhenCompositeExpansionFails_ShouldNotCommitPartialGraph`
- **Test kind**: Unit
- **Expected outcome**: failed expansion leaves the registry unchanged

### Untested Use Cases

None currently tracked.

### Out-of-Scope Use Cases

- Post-startup child module insertion.
