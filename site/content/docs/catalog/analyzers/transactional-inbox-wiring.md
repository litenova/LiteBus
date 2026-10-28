# Transactional Inbox Wiring

## Header

- **ID**: `analyzers.transactional-inbox-dbcontext`
- **Diagnostic**: `LB1016` (Warning)
- **Maturity**: GA
- **Summary**: Reports public constructors that inject `ITransactionalInboxStore` without also injecting a `DbContext` in the same constructor signature.

## Trigger Conditions

`LB1016` reports when:

- A named type declares a public constructor.
- The constructor declares a parameter that implements or equals `ITransactionalInboxStore`.
- The constructor declares no parameter whose type is the Entity Framework Core `DbContext` (`Microsoft.EntityFrameworkCore`) or inherits from it.

## Bad Example

```csharp
using LiteBus.Inbox.Abstractions;

public sealed class OrderService
{
    public OrderService(ITransactionalInboxStore inbox)
    {
    }
}
```

Expected diagnostic:

- `LB1016` on the constructor declaration.

## Good Example

```csharp
using LiteBus.Inbox.Abstractions;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext : DbContext
{
}

public sealed class OrderService
{
    public OrderService(ITransactionalInboxStore inbox, AppDbContext dbContext)
    {
    }
}
```

## Suppression Guidance

- Keep transactional inbox operations inside an active EF unit-of-work boundary.
- Suppress only when transactional inbox is wrapped by another service that already guarantees the scope and the constructor is a false positive.
- Document the unit-of-work boundary next to the suppression.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/TransactionalInboxWithoutDbContextAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `TransactionalInboxWithoutDbContext_ProducesDiagnostic` | Missing `DbContext` alongside transactional inbox store reports `LB1016` |

Untested in this suite:

- The positive path, where the constructor includes `DbContext`.
- Types with multiple constructors where one transactional signature is valid.
