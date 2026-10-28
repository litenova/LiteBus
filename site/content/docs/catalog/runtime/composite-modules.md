# Composite Parent and Child Modules

- **ID**: `runtime.composite-modules`
- **Name**: Composite parent and child modules
- **Maturity**: GA
- **Summary**: A parent module declares its child modules during registration through `ICompositeModule`.

## What It Does

When `IModuleRegistry.Register` receives an `ICompositeModule`, it calls `DeclareChildren(Action<IModule>)` and stages each child immediately after the parent in the same registration call, so a parent and its children always register in the same order.

## Public Surface

| API | Role |
| --- | --- |
| `ICompositeModule` | Parent-child module contract |
| `DeclareChildren(Action<IModule>)` | Child declaration callback |
| `ICompositeModule.BuildOrder` (`CompositeModuleBuildOrder`) | Parent-first by default, reversed for children-first |

## Packages

- `LiteBus.Runtime.Abstractions`
- `LiteBus.Runtime`

## Requires

- `runtime.modules`
- `runtime.module-registry`

## Invariants

- Children are declared during registration, not during build.
- The registry rejects a duplicate child type registration.
- Parent-first build order is the default. Children-first adds the reversed ordering edge before the topological sort.
- A failed composite expansion commits nothing to the registry.

## Non-Goals

- Discovering child modules dynamically at run time.

## Observability

The registry emits no telemetry. Duplicate registrations throw at compose time.

## Test Coverage

### Covered Use Cases

#### `CompositeModuleRegistryTests.Register_CompositeModule_ShouldExpandChildrenImmediatelyAfterParent`
- **Test kind**: Unit
- **Expected outcome**: the registry inserts children after the parent

#### `CompositeModuleRegistryTests.Register_CompositeChildAlsoRegisteredAtTopLevel_ShouldThrowConfigurationException`
- **Test kind**: Unit
- **Expected outcome**: the registry rejects the duplicate child path

#### `CompositeModuleRegistryTests.BuildOrder_WithChildrenFirstComposite_ShouldBuildChildBeforeParent`
- **Test kind**: Unit
- **Expected outcome**: the children-first edge builds the child before the parent

#### `CompositeModuleRegistryTests.BuildOrder_WithNestedOppositeRelationships_ShouldHonorEveryCompositeEdge`
- **Test kind**: Unit
- **Expected outcome**: the registry honors the nested composite ordering edges

#### `CompositeModuleRegistryTests.Register_WhenCompositeExpansionFails_ShouldNotCommitPartialGraph`
- **Test kind**: Unit
- **Expected outcome**: a failed expansion leaves the registry unchanged

### Untested Use Cases

None currently tracked.

### Out-of-Scope Use Cases

- Inserting child modules after startup.
