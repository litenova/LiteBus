# Transactional Outbox Wiring

## Header

- **ID**: `analyzers.transactional-outbox-dbcontext`
- **Diagnostic**: `LB1013` (Warning)
- **Maturity**: GA
- **Summary**: Reports public constructors that inject `ITransactionalOutboxStore` without also injecting a `DbContext` in the same constructor signature.

## Trigger Conditions

`LB1013` reports when:

- A named type declares a public constructor.
- The constructor declares a parameter that implements or equals `ITransactionalOutboxStore`.
- The constructor declares no parameter whose type is the Entity Framework Core `DbContext` (`Microsoft.EntityFrameworkCore`) or inherits from it.

## Bad Example

```csharp
using LiteBus.Outbox.Abstractions;

public sealed class OrderService
{
    public OrderService(ITransactionalOutboxStore outbox)
    {
    }
}
```

Expected diagnostic:

- `LB1013` on the constructor declaration.

## Good Example

```csharp
using LiteBus.Outbox.Abstractions;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext : DbContext
{
}

public sealed class OrderService
{
    public OrderService(ITransactionalOutboxStore outbox, AppDbContext dbContext)
    {
    }
}
```

## Suppression Guidance

- Keep outbox enqueue and domain persistence in the same EF unit of work.
- Suppress only for adapter layers that intentionally resolve `DbContext` through a factory or an ambient scope and cannot express it in constructor parameters.
- Include a comment describing the alternate transaction boundary.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/TransactionalOutboxWithoutDbContextAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `TransactionalOutboxWithoutDbContext_ShouldReportMissingDbContext` | Missing `DbContext` alongside transactional outbox store reports `LB1013` |

Untested in this suite:

- The positive path, where the constructor includes `DbContext`.
- Types with multiple constructors where one is valid and one is invalid.
