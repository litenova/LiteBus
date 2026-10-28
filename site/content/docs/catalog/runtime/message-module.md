# Foundational Messaging Module

- **ID**: `runtime.message-module`
- **Name**: Foundational messaging module
- **Maturity**: GA
- **Summary**: The module bootstraps the shared messaging services, registries, serializer, mediator, and scoped handler registrations.

## What It Does

`MessageModule` creates or reuses the `IMessageRegistry` and contract registry contexts and runs the `MessageModuleBuilder` registration callbacks. It then registers the core messaging services and the newly discovered handler types as scoped dependencies.

## Public Surface

| API | Role |
| --- | --- |
| `ILiteBusBuilder.AddMessaging(Action<MessageModuleBuilder>)` | The normal package-owned entry point for composition |
| `MessageModule(Action<MessageModuleBuilder>)` | Module entry |
| `MessageModuleBuilder.Register<T>()` / `Register(Type)` | Message and handler registration |
| `MessageModuleBuilder.RegisterFromAssembly(Assembly)` | Assembly scan registration |
| `MessageModuleBuilder.Contracts` | Durable contract registration |
| `MessageModuleBuilder.UseTimeProvider(TimeProvider)` | Optional custom clock |
| `MessageModuleBuilder.UseTelemetry(MediationTelemetryOptions)` | Mediation telemetry selection (spans and metrics on, stage spans and metrics opt-in) |
| `MessageModuleBuilder.RequireExplicitOpenGenerics()` | Fails composition on unnamed scan-found open generics |
| `LiteBusCompositionSummary` | Resolvable summary of per-axis message counts, open-generic closures, required declarations, audit configuration, and composition checks |

## Packages

- `LiteBus.Messaging`

## Requires

- `runtime.message-registry`
- `runtime.contract-registry`
- `runtime.dispatch-scopes`

## Invariants

- The message registry is shared per composition context.
- The module registers core services during build.
- The module registers new handlers with scoped lifetime.
- When no host adapter or explicit manual-host opt-in registers an `IMessageDispatchScopeFactory`, composition fails.

## Non-Goals

- Policy for semantic commands, queries, and events.

## Observability

The module exposes no runtime meter.

## Test Coverage

### Covered Use Cases

#### `QueryModulePrerequisiteGuardTests.AddQueryModule_WithoutMessageModule_ShouldFailModuleGraphValidation`
- **Test kind**: Unit
- **Expected outcome**: semantic module registration fails without the message module foundation

#### `HandlerDependencyLifetimeTests.AddLiteBus_WhenMessageAndCommandModulesRegisterSameHandler_ShouldRegisterHandlerAsScoped`
- **Test kind**: Unit
- **Expected outcome**: the shared handler registration stays scoped and stable

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Multiple message-module registration attempts in one graph | Medium | Duplicate module rules block this |

### Out-of-Scope Use Cases

- How the mediator behaves in a distributed deployment.
