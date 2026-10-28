# Module Configuration and Shared Context

- **ID**: `runtime.module-configuration`
- **Name**: Module configuration and shared context
- **Maturity**: GA
- **Summary**: The configuration provides dependency registration, manifest entry collection, and typed context sharing.

## What It Does

`IModuleConfiguration` is passed to every module `Build` call. `ModuleConfiguration` stores the dependency registry, the startup-task, background-service, and diagnostic types, and typed context objects used by modules to share runtime state.

## Public Surface

| API | Role |
| --- | --- |
| `DependencyRegistry` | Service descriptor registration surface |
| `RegisterStartupTask(Type)` | Startup manifest collection |
| `RegisterBackgroundService(Type)` | Background service manifest collection |
| `RegisterDiagnosticCheck(Type, string)` | Diagnostic probe manifest collection |
| `CompositionValidations` | Cross-module rules collected during build |
| `RegisterCompositionValidation(Action)` | Registers a rule that runs after every module has been built |
| `GetContext<T>()`, `SetContext<T>()`, `TryGetContext<T>()`, `GetOrCreateContext<T>()` | Typed context bag |

## Packages

- `LiteBus.Runtime`
- `LiteBus.Runtime.Abstractions`

## Requires

- `runtime.dependency-registry`

## Invariants

- The configuration deduplicates registration lists while preserving first registration order.
- Startup-task types cannot be registered as background services.
- Retrieving a missing context throws `LiteBusConfigurationException`.
- `AddLiteBus` runs every composition validation after the module loop. A custom host adapter must do the same.

## Non-Goals

- Scheduling host execution.

## Observability

The configuration exposes no metric. An invalid registration shape throws argument or configuration exceptions.

## Test Coverage

### Covered Use Cases

#### `ModuleConfigurationTests.RegisterStartupTask_ShouldPreserveFirstRegistrationOrderAndDeduplicate`
- **Test kind**: Unit
- **Expected outcome**: registrations keep a stable deduplicated order

#### `ModuleConfigurationTests.RegisterBackgroundService_WhenTypeImplementsStartupTask_ShouldThrow`
- **Test kind**: Unit
- **Expected outcome**: the configuration rejects the invalid host role

#### `ModuleConfigurationTests.GetOrCreateContext_ForMessageRegistry_ShouldCreateSeparateInstancesPerConfiguration`
- **Test kind**: Unit
- **Expected outcome**: each configuration gets isolated contexts

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Multi-threaded context mutation | Medium | Compose flow is single-threaded by contract |

### Out-of-Scope Use Cases

- Persisting contexts between process restarts.
