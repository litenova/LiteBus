# Module Configuration and Shared Context

- **ID**: `runtime.module-configuration`
- **Name**: Module configuration and shared context
- **Maturity**: GA
- **Summary**: Provides dependency registration, manifest entry collection, and typed context sharing.

## What It Does

`IModuleConfiguration` is passed to every module `Build` call. `ModuleConfiguration` stores the dependency registry, startup/background/diagnostic types, and typed context objects used by modules to share runtime state.

## Public Surface

| API | Role |
| --- | --- |
| `DependencyRegistry` | Service descriptor registration surface |
| `RegisterStartupTask(Type)` | Startup manifest collection |
| `RegisterBackgroundService(Type)` | Background service manifest collection |
| `RegisterDiagnosticCheck(Type, string)` | Diagnostic probe manifest collection |
| `GetContext<T>()`, `SetContext<T>()`, `TryGetContext<T>()`, `GetOrCreateContext<T>()` | Typed context bag |

## Packages

- `LiteBus.Runtime`
- `LiteBus.Runtime.Abstractions`

## Requires

- `runtime.dependency-registry`

## Invariants

- Registration lists are deduplicated while preserving first registration order.
- Startup-task types cannot be registered as background services.
- Missing context retrieval throws `LiteBusConfigurationException`.

## Non-Goals

- Host execution scheduling.

## Observability

No dedicated metric; invalid shape throws argument or configuration exceptions.

## Test Coverage

### Covered Use Cases

#### `ModuleConfigurationTests.RegisterStartupTask_ShouldPreserveFirstRegistrationOrderAndDeduplicate`
- **Test kind**: Unit
- **Expected outcome**: stable deduplicated order

#### `ModuleConfigurationTests.RegisterBackgroundService_WhenTypeImplementsStartupTask_ShouldThrow`
- **Test kind**: Unit
- **Expected outcome**: invalid host role is rejected

#### `ModuleConfigurationTests.GetOrCreateContext_ForMessageRegistry_ShouldCreateSeparateInstancesPerConfiguration`
- **Test kind**: Unit
- **Expected outcome**: per-configuration context isolation

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Multi-threaded context mutation | Medium | Compose flow is single-threaded by contract |

### Out-of-Scope Use Cases

- Persisting contexts between process restarts.
