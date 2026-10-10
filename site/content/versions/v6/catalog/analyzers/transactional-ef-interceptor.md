# Transactional EF Interceptor

## Header

- **ID**: `analyzers.transactional-ef-interceptor`
- **Diagnostic**: `LB1015` (Warning)
- **Maturity**: GA
- **Summary**: Reports transactional EF inbox or outbox storage configuration that calls `EnforceTransactionalSetup()` without `EnableSaveChangesInterceptor()` in the same scope.

## Trigger Conditions

`LB1015` reports when:

- Invocation matches `EnforceTransactionalSetup()` on:
  - `EfCoreInboxStorageModuleBuilder`, or
  - `EfCoreOutboxStorageModuleBuilder`.
- No `EnableSaveChangesInterceptor()` invocation is found in the same enclosing callback or method scope.

## Bad Example

```csharp
public static void Configure(EfCoreOutboxStorageModuleBuilder builder)
{
    builder.EnforceTransactionalSetup();
}
```

Expected diagnostic:

- `LB1015` with axis text `Outbox EF Core storage configuration`.

## Good Example

```csharp
public static void Configure(EfCoreInboxStorageModuleBuilder builder)
{
    builder.EnableSaveChangesInterceptor();
    builder.EnforceTransactionalSetup();
}
```

## Suppression Guidance

- Prefer adding interceptor enablement rather than suppressing.
- Suppress only if interceptor registration occurs in generated or external configuration and cannot be represented in same scope.
- Keep suppression near the affected configuration block.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/TransactionalStorageWithoutInterceptorAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `TransactionalOutboxWithoutInterceptor_ProducesDiagnostic` | Missing interceptor reports `LB1015` |
| `TransactionalInboxWithInterceptor_ProducesNoDiagnostic` | Interceptor in same scope satisfies the rule |
