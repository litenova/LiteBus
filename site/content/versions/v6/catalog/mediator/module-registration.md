# Module Registration

- **ID**: `mediator.module-registration`
- **Name**: Module registration
- **Maturity**: GA
- **Summary**: Registers messaging, command, query, and event features with explicit dependencies and assembly scanning.

## What It Does

Mediator composition is explicit:
1. Call `AddMessaging` once.
2. Call `AddCommands`, `AddQueries`, and/or `AddEvents` from the installed feature packages.

Semantic modules require `MessageModule` to exist. The complete graph validates that requirement after the callback, so declaration order does not affect the result. Registering `MessageModule` twice throws `LiteBusConfigurationException` because type-based module identity permits one instance of each module type.

Builders provide `Register<T>()`, `Register(Type)`, and `RegisterFromAssembly(Assembly)`. Semantic builders expose `Contracts` for stable durable contract registration.

## Public Surface

```csharp
services.AddLiteBus(bus =>
{
    bus.AddMessaging(message =>
    {
        message.RegisterFromAssembly(typeof(CreateOrderCommand).Assembly);
    });

    bus.AddCommands(commands =>
    {
        commands.RegisterFromAssembly(typeof(CreateOrderCommand).Assembly);
    });

    bus.AddQueries(queries =>
    {
        queries.RegisterFromAssembly(typeof(GetOrderByIdQuery).Assembly);
    });

    bus.AddEvents(events =>
    {
        events.RegisterFromAssembly(typeof(OrderPlacedEvent).Assembly);
    });
});
```

| API | Role |
| --- | --- |
| `ILiteBusBuilder.AddMessaging(Action<MessageModuleBuilder>)` | Normal messaging composition extension |
| `ILiteBusBuilder.AddCommands(Action<CommandModuleBuilder>)` | Normal command composition extension |
| `ILiteBusBuilder.AddQueries(Action<QueryModuleBuilder>)` | Normal query composition extension |
| `ILiteBusBuilder.AddEvents(Action<EventModuleBuilder>)` | Normal event composition extension |
| `ILiteBusBuilder.Modules` | Advanced access to the underlying `IModuleRegistry` and `Add*Module` methods |
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

- `MessageModule` must be present in the completed graph when semantic modules are used.
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
- Dependency and duplicate-registration tests in `LiteBus.Mediator.UnitTests`.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `AddMessageModule_WhenCalledTwice_ShouldThrowLiteBusConfigurationException` | `LiteBus.Mediator.UnitTests` |
| `AddCommandModule_WithoutMessageModule_ShouldFailModuleGraphValidation` | `LiteBus.Mediator.UnitTests` |
| `AddQueryModule_WithoutMessageModule_ShouldFailModuleGraphValidation` | `LiteBus.Mediator.UnitTests` |
| `AddEventModule_WithoutMessageModule_ShouldFailModuleGraphValidation` | `LiteBus.Mediator.UnitTests` |
| `AddCommandModule_BeforeMessageModule_ShouldSucceed` and semantic equivalents | `LiteBus.Mediator.UnitTests` |
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
