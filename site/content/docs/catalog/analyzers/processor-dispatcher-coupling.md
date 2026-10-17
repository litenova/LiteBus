# Processor and Dispatcher Coupling

## Header

- **ID**: `analyzers.processor-dispatcher-coupling`
- **Diagnostic**: `LB1014` (Error)
- **Maturity**: GA
- **Summary**: Reports inbox or outbox module configuration that enables processor background execution without dispatcher registration in the same configuration scope.

## Trigger Conditions

`LB1014` reports when:

- Invocation is `EnableInboxProcessor()` on `InboxModuleBuilder`, or
- Invocation is `EnableOutboxProcessor()` on `OutboxModuleBuilder`,
- and no dispatcher registration call is discovered in the same enclosing scope (anonymous callback, method, or constructor).

Recognized dispatcher registrations:

- `UseInProcessDispatch`
- `UseAmqpDispatch`
- `UseInMemoryDispatch`
- `UseAzureServiceBusDispatch`
- `UseAwsSqsDispatch`
- `UseKafkaDispatch`
- `RegisterDispatcher`

Dispatcher calls are recognized for direct builder methods and extension methods targeting inbox or outbox module builders.

## Bad Example

```csharp
registry.AddInboxModule(inbox =>
{
    inbox.EnableInboxProcessor();
});
```

Expected diagnostic:

- `LB1014` on `EnableInboxProcessor()`.

## Good Example

```csharp
registry.AddOutboxModule(outbox =>
{
    outbox.UseInProcessDispatch();
    outbox.EnableOutboxProcessor();
});
```

## Suppression Guidance

- Prefer registering dispatcher in the same callback where processor is enabled.
- If dispatcher registration is intentionally split to another method, keep a narrow suppression at processor enablement call and document the paired registration.
- Do not suppress for missing dispatch modules, processor execution without dispatcher is invalid at runtime.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/ProcessorEnabledWithoutDispatcherAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `InboxProcessorWithoutDispatcher_ProducesDiagnostic` | Missing inbox dispatcher reports `LB1014` |
| `InboxProcessorWithDispatcher_ProducesNoDiagnostic` | `RegisterDispatcher` in same scope satisfies rule |
| `InboxProcessorWithUseInProcessDispatch_ProducesNoDiagnostic` | `UseInProcessDispatch` in same scope satisfies rule |
| `OutboxProcessorWithoutDispatcher_ProducesDiagnostic` | Missing outbox dispatcher reports `LB1014` |
| `OutboxProcessorWithUseInProcessDispatch_ProducesNoDiagnostic` | Outbox `UseInProcessDispatch` in same scope satisfies rule |

Untested in this suite:

- Broker-specific dispatcher registrations (`UseAmqpDispatch`, `UseKafkaDispatch`, `UseAwsSqsDispatch`, `UseAzureServiceBusDispatch`, `UseInMemoryDispatch`) are recognized by analyzer logic, but no dedicated fixtures exist yet.
