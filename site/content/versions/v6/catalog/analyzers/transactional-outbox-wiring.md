# Transactional Outbox Wiring

## Header

- **ID**: `analyzers.transactional-outbox-dbcontext`
- **Diagnostic**: `LB1013` (Warning)
- **Maturity**: GA
- **Summary**: Reports public constructors that inject `ITransactionalOutboxStore` without also injecting a `DbContext` in the same constructor signature.

## Trigger Conditions

`LB1013` reports when:

- Named type has a public constructor.
- Constructor has a parameter implementing or equal to `ITransactionalOutboxStore`.
- Constructor has no parameter type that inherits from `DbContext`.

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

- `LB1013` on constructor declaration.

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
- Suppress only for adapter layers that intentionally resolve `DbContext` through factory or ambient scope and cannot express it in constructor parameters.
- Include a comment describing the alternate transaction boundary.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/TransactionalOutboxWithoutDbContextAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `TransactionalOutboxWithoutDbContext_ShouldReportMissingDbContext` | Missing `DbContext` alongside transactional outbox store reports `LB1013` |

Untested in this suite:

- Positive path where constructor includes `DbContext`.
- Multi-constructor types where one constructor is valid and one is invalid.
