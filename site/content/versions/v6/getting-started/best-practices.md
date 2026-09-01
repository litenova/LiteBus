# Best Practices

These are the conventions that keep a LiteBus codebase readable as it grows: how to shape messages, where to put logic, and how to test. Each one links to the page that explains the mechanism in depth. Read this after you are comfortable with the [Command Module](../concepts/commands.md), [Query Module](../concepts/queries.md), and [Event Module](../concepts/events.md).

## 1. Message Design

- **Immutability**: Design your command, query, and event objects to be immutable. Use C# records or classes with `init`-only properties. This prevents state from being accidentally modified during processing.
  ```csharp
  // Good: Immutable record
  public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<Guid>;
  ```
- **Clarity and Focus**: Each message should represent a single, well-defined action or piece of information. Avoid creating generic "god" messages that do too many things.
- **Naming Conventions**:
  - **Commands**: Use imperative verbs in the present tense (e.g., `CreateUser`, `UpdateAddress`).
  - **Queries**: Use descriptive nouns (e.g., `GetProductById`, `FindUsersByRole`).
  - **Events**: Use verbs in the past tense (e.g., `OrderShipped`, `UserRegistered`).
- **Return DTOs, Not Domain Entities**: Query handlers should return Data Transfer Objects (DTOs) or view models, not your internal domain entities. This creates a clean public contract for your application layer and prevents leaking domain logic.

## 2. Handler Design

- **Single Responsibility**: A handler should do one thing well. A command handler should execute its business logic; a pre-handler should validate; a post-handler should handle side effects like notifications.
- **Idempotency**: For critical operations (especially with the Command Inbox or in distributed systems), design your handlers to be idempotent. This means they can be safely executed multiple times with the same input without causing incorrect results.
- **Dependency Injection**: Handlers should be stateless. All dependencies (repositories, services, etc.) should be injected via the constructor.
- **Avoid Chaining**: Avoid having one handler call the mediator to send another message. This creates "magic" action-at-a-distance and makes the control flow very difficult to follow. For complex workflows, use a Saga or Process Manager pattern instead.

## 3. Pipeline Management

- **Use Pre-Handlers for Guard Clauses**: Pre-handlers are the ideal place for validation, permission checks, and other "guard clauses" that should run before any business logic.
- **Use Post-Handlers for Side Effects**: Post-handlers are perfect for operations that should occur *after* the main transaction has completed, such as publishing integration events, sending notifications, or clearing caches.
- **Centralize Cross-Cutting Concerns**: Use polymorphic dispatch on base interfaces (e.g., `IAuditableCommand`) to implement cross-cutting concerns like auditing and authorization in a single place.
- **Use Open Generic Handlers for Universal Concerns**: For cross-cutting logic that should apply to *all* messages of a given type (e.g., logging every command, validating every command), use open generic handlers instead of polymorphic dispatch. This avoids requiring each message to implement a shared interface.
  ```csharp
  // Single handler that applies to ALL commands; no interface changes needed
  public sealed class CommandLogger<T> : ICommandPreHandler<T> where T : ICommand { /* ... */ }
  module.Register(typeof(CommandLogger<>));
  ```
- **Prefer Open Generics over Duplicate Handlers**: If you find yourself writing the same pre/post handler logic for multiple message types, consolidate into a single open generic handler.

## 4. Error Handling

- **Throw Specific Exceptions**: In your handlers, throw specific, custom exceptions (e.g., `ProductNotFoundException`) instead of generic ones.
- **Use Error Handlers for Cross-Cutting Error Logic**: Implement `ICommandErrorHandler` (or query/event equivalents) to handle logging, metrics, or transforming exceptions into user-friendly error responses in a centralized way.

## 5. Configuration and Testing

- **Register Handlers at Startup**: Configure all your LiteBus modules and register your handlers during application startup for optimal performance.
- **Isolate Tests**: Use the narrow `LiteBus.Testing.*` package for the concern under test, or create a separate `AddLiteBus` host (or new `MessageRegistry`) per test so handler registrations do not leak across tests.
- **Unit Test Logic, Integration Test Pipelines**: Write fine-grained unit tests for the business logic inside your handlers. Write a smaller number of integration tests to verify that your most important pipelines (including pre/post handlers) are wired correctly.

## Next
