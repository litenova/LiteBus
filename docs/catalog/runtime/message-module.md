# Foundational Messaging Module

- **ID**: `runtime.message-module`
- **Name**: Foundational messaging module
- **Maturity**: GA
- **Summary**: Bootstraps shared messaging services, registries, serializer, mediator, and scoped handler registrations.

## What It Does

`MessageModule` creates or reuses `IMessageRegistry` and contract registry contexts, executes `MessageModuleBuilder` registration callbacks, registers core messaging services, and registers newly discovered handler types as scoped dependencies.

## Public Surface

| API | Role |
| --- | --- |
| `ILiteBusBuilder.AddMessaging(Action<MessageModuleBuilder>)` | Normal package-owned composition entry |
| `MessageModule(Action<MessageModuleBuilder>)` | Module entry |
| `MessageModuleBuilder.Register<T>()` / `Register(Type)` | Message and handler registration |
| `MessageModuleBuilder.RegisterFromAssembly(Assembly)` | Assembly scan registration |
| `MessageModuleBuilder.Contracts` | Durable contract registration |
| `MessageModuleBuilder.UseTimeProvider(TimeProvider)` | Optional custom clock |

## Packages

- `LiteBus.Messaging`

## Requires

- `runtime.message-registry`
- `runtime.contract-registry`
- `runtime.dispatch-scopes`

## Invariants

- Message registry is shared per composition context.
- Core services are registered during build.
- New handlers are registered with scoped lifetime.
- Composition fails when no `IMessageDispatchScopeFactory` is registered by a host adapter or explicit manual-host opt-in.

## Non-Goals

- Semantic command/query/event policy.

## Observability

No dedicated runtime meter.

## Test Coverage

### Covered Use Cases

#### `QueryModulePrerequisiteGuardTests.AddQueryModule_WithoutMessageModule_ShouldFailModuleGraphValidation`
- **Test kind**: Unit
- **Expected outcome**: semantic module registration is blocked without message module foundation

#### `HandlerDependencyLifetimeTests.AddLiteBus_WhenMessageAndCommandModulesRegisterSameHandler_ShouldRegisterHandlerAsScoped`
- **Test kind**: Unit
- **Expected outcome**: shared handler registration remains scoped and stable

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Multiple message-module registration attempts in one graph | Medium | Blocked by duplicate module rules |

### Out-of-Scope Use Cases

- Distributed mediator deployment behavior.
