# Command Module

A command asks LiteBus to change state through exactly one handler. This page shows you how to define a command, handle it, validate it, send it, and defer it for later execution.

> **This page is for** developers who write command handlers.
>
> **After reading it** you can define, handle, and send a command. Continue with [Queries](queries.md).

## Define a Command

```csharp
public sealed record UpdateStockLevelCommand(Guid ProductId, int NewQuantity) : ICommand;

public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<ProductDto>;
```

You pick one of two contracts. Use `ICommand` when the caller only needs success or failure. Use `ICommand<TResult>` when the caller needs a value back, most often the identity of something just created. Model commands as immutable records or classes with `init`-only properties so no step can change them after construction.

Two rules define a command. It is handled by exactly one handler, and it expresses an intent to change something. When zero handlers or more than one handler is registered, mediation throws rather than guessing. Name commands as imperatives, such as `CreateProductCommand` or `CancelSubscriptionCommand`.

Returning a value from a state-changing command is a deliberate exception to strict CQS. LiteBus allows it because returning a generated id from the same call beats forcing a follow-up query. Keep the returned value small. A command is not a query.

## Handle a Command

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

| Command | Handler interface | Method |
| --- | --- | --- |
| `ICommand` | `ICommandHandler<TCommand>` | `Task HandleAsync(TCommand, CancellationToken)` |
| `ICommand<TResult>` | `ICommandHandler<TCommand, TResult>` | `Task<TResult> HandleAsync(TCommand, CancellationToken)` |

The handler holds the business logic and is the one place a command runs. Inject dependencies through the constructor. Handlers resolve from the container per mediation, so a scoped dependency such as a database context works as expected inside a request scope.

## Validate Before You Handle

```csharp
public sealed class CreateProductValidator : ICommandValidator<CreateProductCommand>
{
    public Task<Validity> ValidateAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Price <= 0)
            return Task.FromResult(Validity.Invalid("Price must be positive.", nameof(command.Price)));

        return Task.FromResult(Validity.Valid);
    }
}
```

A validator answers whether a command is well formed and nothing else. Return `Validity.Valid` when nothing is wrong and `Validity.Invalid` with each failure you found. Every validator runs and LiteBus collects the failures, so the caller sees all of them at once. A failed validation reports `MediationOutcome.Invalid` and never reaches the main handler.

Use `ICommandPreHandler<TCommand>` with `PreHandleAsync` for checks that act rather than judge shape, such as confirming a name is still unique. Throwing in a pre-handler stops the pipeline before any state changes.

## React After You Handle

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

A post-handler runs after the main handler returns successfully and receives the command plus its result. Use it to publish a follow-up event or refresh a cache. Two shapes exist: `ICommandPostHandler<TCommand, TResult>` receives the typed result and `ICommandPostHandler<TCommand>` receives it as `object?`.

```csharp
public sealed class LogPaymentFailure : ICommandErrorHandler<ProcessPaymentCommand>
{
    private readonly ILogger<LogPaymentFailure> _logger;

    public LogPaymentFailure(ILogger<LogPaymentFailure> logger) => _logger = logger;

    public Task HandleErrorAsync(
        MessageErrorContext<ProcessPaymentCommand, object> context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogError(context.Exception, "Payment failed.");
        return Task.CompletedTask;
    }
}
```

An error-handler runs when any stage throws. By default the outcome stays `MessageErrorOutcome.Unhandled` and LiteBus rethrows the original exception with its stack trace intact. Set `context.Outcome` to `MessageErrorOutcome.Handled` to recover, and for a result-bearing command also set `context.HandledResult` to the fallback value the caller receives.

## Send a Command

```csharp
public async Task<ActionResult<ProductDto>> Create(
    CreateProductCommand command,
    [FromServices] ICommandMediator commandMediator,
    CancellationToken cancellationToken)
{
    var product = await commandMediator.SendAsync(command, cancellationToken: cancellationToken);
    return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
}
```

Inject `ICommandMediator` and call `SendAsync`. The signatures you call most are:

```csharp
Task SendAsync(ICommand command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default);
Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default);
```

Pass `CommandMediationSettings` to filter handlers per call with tags or a predicate, or to carry context items through the pipeline. See [Handler Filtering](handler-filtering.md). When a refusal is a routine branch rather than a fault, call `TrySendAsync` and read the outcome value instead of catching. When you need to ask whether a command would be permitted without running it, call `EvaluateAsync`.

## Defer a Command to the Inbox

```csharp
var receipt = await inbox.AcceptAsync(
    InboxAcceptItem<ProcessPaymentCommand>.From(
        new ProcessPaymentCommand(orderId, amount),
        InboxAcceptMetadata.Immediate with
        {
            Idempotency = new Idempotency.Keyed($"payment:{orderId}")
        }),
    cancellationToken);
```

`SendAsync` runs the command now in the current process. `IInbox.AcceptAsync` stores it instead and returns a receipt, and a processor runs it later with at-least-once delivery. Defer only commands without a result: a stored command runs with no caller waiting for a value. Make deferred handlers safe to run twice, keyed on an idempotency value. The stores, dispatch adapters, and the processor are covered on [Inbox](../reliable-messaging/inbox.md).

## Details

- The full stage order, including guards, shortcuts, global versus specific handlers, and refusal mapping, lives on [The Handler Pipeline](handler-pipeline.md). That page is authoritative when this page and that page seem to differ.
- Keep commands to one business action, define them as immutable messages, and reject bad input in validators and pre-handlers before the main handler runs.

## Next

- [Queries](queries.md) for reads with a single result or a stream
- [The Handler Pipeline](handler-pipeline.md) for the full stage order
- [Inbox](../reliable-messaging/inbox.md) for durable command execution
