# Best Practices

A LiteBus codebase stays readable as it grows when messages, handlers, and tests follow shared conventions for shaping messages, placing logic, and testing. Each convention below links to the page that explains its mechanism in depth. Read this page after you know the [Command Module](../concepts/commands.md), [Query Module](../concepts/queries.md), and [Event Module](../concepts/events.md).

## 1. Message Design

- **Immutability**: Make command, query, and event objects immutable. Use C# records or classes with `init`-only properties, so no handler can change a message while it is being processed.
  ```csharp
  // Good: Immutable record
  public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<Guid>;
  ```
- **Clarity and Focus**: Give each message one business action or one piece of information. Split a message that carries unrelated operations.
- **Naming Conventions**:
  - **Commands**: Use imperative verbs in the present tense (e.g., `CreateUser`, `UpdateAddress`).
  - **Queries**: Use descriptive nouns (e.g., `GetProductById`, `FindUsersByRole`).
  - **Events**: Use verbs in the past tense (e.g., `OrderShipped`, `UserRegistered`).
- **Return DTOs, Not Domain Entities**: A query handler should return Data Transfer Objects (DTOs) or view models, not internal domain entities. That gives the application layer a public contract without exposing domain logic.

## 2. Handler Design

- **Single Responsibility**: A handler should do one thing well. A command handler executes its business logic, a guard decides permission, a validator judges well-formedness, and a post-handler reacts to success with side effects such as notifications.
- **Idempotency**: Design handlers for critical operations to be idempotent, especially with the Command Inbox or in distributed systems. An idempotent handler can run multiple times with the same input without producing incorrect results.
- **Dependency Injection**: Keep handlers stateless. Inject all dependencies, such as repositories and services, through the constructor.
- **Avoid Chaining**: Do not call the mediator from inside a handler to send another message. That hides control flow from the caller, because the second message never appears at the call site. For complex workflows, use a saga instead.

## 3. Pipeline Management

- **Use Guards and Validators for Decisions**: Use guards for permission checks and validators for well-formedness. Both run before pre-handlers and report refusals as values rather than exceptions. Use pre-handlers only to prepare a message for handling.
- **Use Post-Handlers for Reactions Inside the Transaction**: Use post-handlers for work that follows a successful main handler, such as publishing follow-up events, refreshing projections, or clearing caches. Post-handlers run before the completion stage, so a commit placed in a post-handler runs before the completion stage reports any failure. Instead, commit from a completion handler at `HandlerPriorities.UnitOfWork`, and condition the commit on the outcome.
- **Centralize Cross-Cutting Concerns**: Implement cross-cutting concerns such as auditing and authorization in one place with polymorphic dispatch on base interfaces (e.g., `IAuditableCommand`).
- **Use Open Generic Handlers for Universal Concerns**: For cross-cutting logic that applies to all messages of a given type (e.g., logging every command, validating every command), use open generic handlers instead of polymorphic dispatch. Messages then need no shared interface.
  ```csharp
  // Single handler that applies to ALL commands; no interface changes needed
  public sealed class CommandLogger<T> : ICommandPreHandler<T> where T : ICommand { /* ... */ }
  module.Register(typeof(CommandLogger<>));
  ```
- **Prefer Open Generics over Duplicate Handlers**: If several message types need the same pre- or post-handler logic, consolidate it into one open generic handler.

## 4. Error Handling

- **Throw Specific Exceptions**: In handlers, throw specific custom exceptions (e.g., `ProductNotFoundException`) instead of generic ones.
- **Use Error Handlers for Cross-Cutting Error Logic**: Implement `ICommandErrorHandler` (or the query and event equivalents) to centralize logging, metrics, and the transformation of exceptions into user-friendly error responses.

## 5. Configuration and Testing

- **Register Handlers at Startup**: Configure all LiteBus modules and register handlers during application startup, so the assembly scan runs once.
- **Isolate Tests**: Use the `LiteBus.Testing.*` package that matches the concern under test, or create a separate `AddLiteBus` host (or new `MessageRegistry`) per test so handler registrations do not leak across tests.
- **Unit Test Logic, Integration Test Pipelines**: Write fine-grained unit tests for the business logic inside handlers, and fewer integration tests to verify that the most important pipelines, including pre- and post-handlers, are wired correctly.

## Next

- [Cookbook and Scenarios](cookbook.md) for lookup-style recipes that apply these conventions
- [Cheat Sheet](cheat-sheet.md) for the same APIs on one page
- [The Handler Pipeline](../concepts/handler-pipeline.md) for stage order and decisions
