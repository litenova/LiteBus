# Command Module

A command is a request to change state: create an order, update stock, cancel a subscription. This page covers the command side of LiteBus in full: the command contracts, the handler interfaces, the four pipeline stages a command passes through, and how to send one. It is for a developer writing command handlers. You should have read [Getting Started](../getting-started/README.md) first.

A command in LiteBus has two defining rules. It is handled by exactly one handler, and it expresses an intent to change something. If zero handlers or more than one handler are registered for a command, mediation throws rather than guessing. Name commands as imperatives: `CreateProductCommand`, `UpdateStockLevelCommand`, `CancelSubscriptionCommand`.

## Command Contracts

A command implements one of two interfaces depending on whether the caller needs a return value.

### `ICommand` (No Result)

Use `ICommand` when the caller only needs to know the operation succeeded. Success is the absence of an exception; failure is an exception. Model the command as an immutable record or a class with `init`-only properties so it cannot change after construction.

```csharp
public sealed record UpdateStockLevelCommand(Guid ProductId, int NewQuantity) : ICommand;
```

### `ICommand<TResult>`

Use `ICommand<TResult>` when the caller needs a value back, most often the identity of something just created.

```csharp
public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<ProductDto>;
```

Returning a result from a state-changing command is a deliberate exception to strict CQS. LiteBus allows it because returning a generated id or a created DTO from the same call is more practical than forcing a follow-up query. Keep the returned value small; a command is not a query.

## Command Handlers

The handler holds the business logic and is the one place a command is processed. There is one handler interface per command shape.

| Command | Handler interface | Method |
| --- | --- | --- |
| `ICommand` | `ICommandHandler<TCommand>` | `Task HandleAsync(TCommand, CancellationToken)` |
| `ICommand<TResult>` | `ICommandHandler<TCommand, TResult>` | `Task<TResult> HandleAsync(TCommand, CancellationToken)` |

```csharp
public sealed class UpdateStockLevelCommandHandler : ICommandHandler<UpdateStockLevelCommand>
{
    private readonly IProductRepository _repository;

    public UpdateStockLevelCommandHandler(IProductRepository repository) => _repository = repository;

    public async Task HandleAsync(UpdateStockLevelCommand command, CancellationToken cancellationToken = default)
    {
        var product = await _repository.GetAsync(command.ProductId, cancellationToken);
        product.SetStock(command.NewQuantity);
        await _repository.SaveAsync(product, cancellationToken);
    }
}
```

```csharp
public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, ProductDto>
{
    private readonly IProductRepository _repository;

    public CreateProductCommandHandler(IProductRepository repository) => _repository = repository;

    public async Task<ProductDto> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        var product = new Product(command.Name, command.Price);
        await _repository.SaveAsync(product, cancellationToken);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

Inject dependencies through the constructor. Handlers resolve from the container per mediation, so a scoped `DbContext` works as expected inside a request scope.

## The Command Pipeline

Around that single main handler, a command passes through three optional stages. Each stage is a separate interface you implement and register; LiteBus discovers them during `RegisterFromAssembly`. The order for one command is: pre-handlers, main handler, post-handlers, with error-handlers invoked instead of post-handlers when any stage throws.

### Pre-Handlers and Validators

A pre-handler runs before the main handler. Throwing in a pre-handler stops the pipeline before any state changes, which makes pre-handlers the place for validation and authorization.

`ICommandValidator<TCommand>` is a pre-handler with a name that documents intent. It is identical in behavior to `ICommandPreHandler<TCommand>`; choose the validator interface when the step's only job is to reject bad input.

```csharp
public sealed class CreateProductValidator : ICommandValidator<CreateProductCommand>
{
    public Task ValidateAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Price <= 0)
            throw new ValidationException("Price must be positive.");

        return Task.CompletedTask;
    }
}
```

```csharp
public sealed class EnsureNameIsUnique : ICommandPreHandler<CreateProductCommand>
{
    private readonly IProductRepository _repository;

    public EnsureNameIsUnique(IProductRepository repository) => _repository = repository;

    public async Task PreHandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (await _repository.NameExistsAsync(command.Name, cancellationToken))
            throw new ConflictException($"A product named '{command.Name}' already exists.");
    }
}
```

### Post-Handlers

A post-handler runs after the main handler returns successfully. It receives the command and the result, which makes it the natural place to publish a follow-up event or invalidate a cache. Two shapes exist: `ICommandPostHandler<TCommand, TResult>` receives the typed result, `ICommandPostHandler<TCommand>` receives the result as `object?`.

```csharp
public sealed class PublishProductCreated : ICommandPostHandler<CreateProductCommand, ProductDto>
{
    private readonly IEventMediator _eventMediator;

    public PublishProductCreated(IEventMediator eventMediator) => _eventMediator = eventMediator;

    public Task PostHandleAsync(CreateProductCommand command, ProductDto? result, CancellationToken cancellationToken = default)
    {
        return result is null
            ? Task.CompletedTask
            : _eventMediator.PublishAsync(new ProductCreatedEvent(result.Id), cancellationToken);
    }
}
```

### Error-Handlers

An error-handler runs when any stage throws. Implement `ICommandErrorHandler<TCommand>`; its typed context contains the command, the partial result if any, the exception, and the shared recovery outcome. The caller's cancellation token is passed explicitly. The original exception is rethrown with its stack trace intact unless a handler sets `Outcome` to `MessageErrorOutcome.Handled`.

```csharp
public sealed class LogPaymentFailure : ICommandErrorHandler<ProcessPaymentCommand>
{
    private readonly ILogger<LogPaymentFailure> _logger;

    public LogPaymentFailure(ILogger<LogPaymentFailure> logger) => _logger = logger;

    public Task HandleErrorAsync(
        MessageErrorContext<ProcessPaymentCommand, object> context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogError(context.Exception, "Payment failed for order {OrderId}", context.Message.OrderId);
        return Task.CompletedTask; // Outcome remains Unhandled, so LiteBus rethrows the original exception
    }
}
```

To recover, set `context.Outcome = MessageErrorOutcome.Handled`. A result-bearing command handler must also set `context.HandledResult` to the fallback value the caller should receive.

The exact ordering of these stages, including global versus specific handlers and how `Abort` short-circuits the pipeline, is on [The Handler Pipeline](handler-pipeline.md).

## Sending a Command

Inject `ICommandMediator` and call `SendAsync`. The overload you call depends on whether the command returns a result.

```csharp
public interface ICommandMediator
{
    Task SendAsync(ICommand command, CommandMediationSettings? settings = null, CancellationToken cancellationToken = default);
    Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command, CommandMediationSettings? settings = null, CancellationToken cancellationToken = default);
}
```

```csharp
[ApiController]
[Route("products")]
public class ProductsController : ControllerBase
{
    private readonly ICommandMediator _commandMediator;

    public ProductsController(ICommandMediator commandMediator) => _commandMediator = commandMediator;

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(CreateProductCommand command)
    {
        var product = await _commandMediator.SendAsync(command);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [HttpPut("{id}/stock")]
    public async Task<IActionResult> UpdateStock(Guid id, UpdateStockLevelCommand command)
    {
        await _commandMediator.SendAsync(command);
        return NoContent();
    }
}
```

The optional `CommandMediationSettings` carries handler filter tags and per-call context items; see [Handler Filtering](handler-filtering.md).

## Deferring a Command to the Inbox

`SendAsync` always runs the command now, in the current process. When you instead want to accept a command, store it, and execute it later with at-least-once delivery, use `IInbox.AcceptAsync`. Only `ICommand` (no result) can be stored, because a future execution has no caller to return a result to.

```csharp
var receipt = await inbox.AcceptAsync(
    InboxAcceptItem<ProcessPaymentCommand>.From(
        new ProcessPaymentCommand(orderId, amount),
        InboxAcceptMetadata.Immediate with
        {
            Idempotency = new Idempotency.Keyed($"payment:{orderId}"),
        }),
    cancellationToken);
```

The inbox, its stores, dispatch adapters, and the processor that drains it are covered in full on [Inbox](../reliable-messaging/inbox.md).

## Shared Features

The pipeline mechanics below apply to commands, queries, and events alike. Each has a dedicated page:

- [Handler Priority](handler-priority.md): order pre- and post-handlers with `[HandlerPriority]`.
- [Handler Filtering](handler-filtering.md): select handlers per call with tags or predicates.
- [Execution Context](execution-context.md): share state across handlers and override results.
- [Polymorphic Dispatch](polymorphic-dispatch.md): handle a base command type for all derived commands.
- [Generic Messages & Handlers](generic-messages-and-handlers.md) and [Open Generic Handlers](open-generic-handlers.md): reusable command handlers and cross-cutting steps.
- [Domain Events and Unit of Work](domain-events-and-unit-of-work.md): raise domain events from aggregates near the transaction boundary.

## Best Practices

- **Immutability.** Define commands as records or `init`-only classes. A command is a message, not a mutable buffer.
- **One operation.** A command should represent a single business action. Splitting unrelated changes keeps handlers and failures easy to reason about.
- **Validate in pre-handlers.** Reject bad input before the main handler runs, so state never changes on invalid commands.
- **Idempotency for retried work.** Commands processed through the inbox can be delivered more than once. Make their handlers safe to run twice, keyed on an idempotency value.

## Next
