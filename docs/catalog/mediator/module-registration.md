# Module Registration

- **ID**: `mediator.module-registration`
- **Name**: Module registration
- **Maturity**: GA
- **Summary**: Registers message, command, query, and event modules with prerequisite guards and assembly scanning.

## What It Does

Mediator composition is explicit:
1. Register `MessageModule` once.
2. Register `CommandModule`, `QueryModule`, and/or `EventModule`.

Semantic modules require `MessageModule` to exist. Attempting to register semantic modules first throws `LiteBusConfigurationException`. Registering `MessageModule` twice also throws `LiteBusConfigurationException`.

Builders provide `Register<T>()`, `Register(Type)`, and `RegisterFromAssembly(Assembly)`. Semantic builders expose `Contracts` for stable durable contract registration.

## Public Surface

```csharp
services.AddLiteBus(registry =>
{
    registry.AddMessageModule(message =>
    {
        message.RegisterFromAssembly(typeof(CreateOrderCommand).Assembly);
    });

    registry.AddCommandModule(commands =>
    {
        commands.RegisterFromAssembly(typeof(CreateOrderCommand).Assembly);
    });

    registry.AddQueryModule(queries =>
    {
        queries.RegisterFromAssembly(typeof(GetOrderByIdQuery).Assembly);
    });

    registry.AddEventModule(events =>
    {
        events.RegisterFromAssembly(typeof(OrderPlacedEvent).Assembly);
    });
});
```

| API | Role |
| --- | --- |
| `ModuleRegistryExtensions.AddMessageModule(Action<MessageModuleBuilder>)` | Registers core message module |
| `ModuleRegistryExtensions.AddCommandModule(Action<CommandModuleBuilder>)` | Registers command module |
| `ModuleRegistryExtensions.AddQueryModule(Action<QueryModuleBuilder>)` | Registers query module |
| `ModuleRegistryExtensions.AddEventModule(Action<EventModuleBuilder>)` | Registers event module |
| `ModuleRegistryExtensions.AddEventModule()` | Registers event module with default builder action |
| `MessageModuleBuilder.RegisterFromAssembly(Assembly)` | Registers messages and handlers in one pass |
| `CommandModuleBuilder.RegisterFromAssembly(Assembly)` | Registers command constructs from assembly |
| `QueryModuleBuilder.RegisterFromAssembly(Assembly)` | Registers query constructs from assembly |
| `EventModuleBuilder.RegisterFromAssembly(Assembly)` | Registers event constructs from assembly |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Commands`
- `LiteBus.Queries`
- `LiteBus.Events`

## Requires

- `runtime.modules`
- `runtime.message-module`

## Invariants

- `MessageModule` must be registered before semantic modules.
- `MessageModule` can only be registered once.
- Builder `RegisterFromAssembly` throws `ArgumentNullException` for null assembly arguments.
- Semantic builder registration rejects unsupported construct types (`LiteBusNotSupportedException` for wrong shape).

## Non-Goals

- Implicit auto-registration of semantic modules.
- Global assembly auto-scan without explicit module builder calls.
- Multi-host module graph synchronization.

## Observability

No dedicated compose-time metric or activity source is exposed for mediator module registration.

Operational alternatives:
- Fail-fast exception handling at startup.
- Registration guard tests in `LiteBus.Mediator.UnitTests`.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `AddMessageModule_WhenCalledTwice_ShouldThrowLiteBusConfigurationException` | `LiteBus.Mediator.UnitTests` |
| `AddCommandModule_WithoutMessageModule_ShouldThrowLiteBusConfigurationException` | `LiteBus.Mediator.UnitTests` |
| `AddQueryModule_WithoutMessageModule_ShouldThrowLiteBusConfigurationException` | `LiteBus.Mediator.UnitTests` |
| `AddEventModule_WithoutMessageModule_ShouldThrowLiteBusConfigurationException` | `LiteBus.Mediator.UnitTests` |
| `RegisterFromAssembly_WithNullAssembly_ThrowsArgumentNullException` | `LiteBus.Mediator.UnitTests` |
| `RegisterFromAssembly_DoesNotRegisterMarkerInterfaces` | `LiteBus.Mediator.UnitTests` |

### Untested

- Very large assembly scans with high generic handler counts and cold start timing assertions.
- Mixed registration from many assemblies with conflicting simple type names.

### Out-of-Scope

- Host-specific DI registration details outside `AddLiteBus` module composition.
- Runtime hot-reload of module graph.

## Deep Docs

- [Getting started](../../getting-started/README.md)
- [Command module](../../concepts/commands.md)
- [Query module](../../concepts/queries.md)
- [Event module](../../concepts/events.md)
