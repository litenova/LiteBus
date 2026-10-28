# Cross-Assembly Handlers

## Header

- **ID**: `analyzers.cross-assembly-handler-name`
- **Diagnostic**: `LB1012` (Warning)
- **Maturity**: GA
- **Summary**: Warns when the same handler simple name appears in different assemblies, where assembly scanning can register it ambiguously or twice.

## Trigger Conditions

`LB1012` reports when:

- The analyzer collects handler registrations from source and from eligible referenced assemblies.
- Two or more registrations share the same handler simple name (`HandlerType.Name`).
- Those registrations come from different assemblies.

The warning compares simple names, not full type identity.

## Bad Example

```csharp
// AssemblyA
public sealed class SharedHandlerName : ICommandHandler<CommandA> { ... }

// AssemblyB
public sealed class SharedHandlerName : ICommandHandler<CommandB> { ... }
```

Expected diagnostic:

- `LB1012` for `SharedHandlerName` with both assembly names in the message.

## Good Example

```csharp
public sealed class BillingCreateOrderHandler : ICommandHandler<CreateOrderCommand> { ... }
public sealed class FulfillmentCreateShipmentHandler : ICommandHandler<CreateShipmentCommand> { ... }
```

Distinct handler names keep scan-based registration from matching the wrong handler at composition.

## Suppression Guidance

- Prefer unique, domain-specific handler names per assembly.
- If duplicate names are intentional, avoid broad assembly scanning or isolate registration scopes.
- Suppress only when registration paths are deterministic and reviewed.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/DuplicateHandlerAcrossAssembliesAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `DuplicateHandlerAcrossAssemblies_ShouldReportWhenNameExistsInTwoAssemblies` | Duplicate command handler name across two assemblies reports `LB1012` |
| `DuplicateHandlerNameInSameAssembly_ShouldNotReport` | Name differences in same assembly do not report |
| `DuplicateEventHandlerAcrossAssemblies_ShouldReportWhenNameExistsInTwoAssemblies` | Duplicate event handler name across two assemblies reports `LB1012` |
