# Commands

- **ID**: `mediator.commands`
- **Name**: Commands
- **Maturity**: GA
- **Summary**: Executes state-changing messages through exactly one main handler, plus pre, post, and error stages.

## What It Does

`ICommandMediator` sends command messages through `SingleAsyncHandlerMediationStrategy`. Commands require exactly one main handler after routing filters are applied. When no handler or multiple handlers remain, mediation fails fast (`NoHandlerFoundException` or `MultipleHandlerFoundException`).

Commands support both void (`ICommand`) and result (`ICommand<TResult>`) flows. A gate can stop execution before the handler runs, as an early answer or a refusal. Post-handlers can observe results, and result post-handlers can replace the returned value through `AmbientExecutionContext.Current.MessageResult`.

## Public Surface

```csharp
public sealed record CreateProductCommand(string Name) : ICommand<Guid>;

public sealed class CreateProductHandler : ICommandHandler<CreateProductCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
        => Task.FromResult(Guid.NewGuid());
}

var id = await commandMediator.SendAsync(new CreateProductCommand("keyboard"), cancellationToken);
```

| API | Role |
| --- | --- |
| `ICommand` | Void command contract |
| `ICommand<TResult>` | Result command contract |
| `ICommandMediator.SendAsync(ICommand, CommandMediationSettings?, CancellationToken)` | Send void command |
| `ICommandMediator.SendAsync<TResult>(ICommand<TResult>, CommandMediationSettings?, CancellationToken)` | Send result command |
| `ICommandHandler<TCommand>` | Main handler for void command |
| `ICommandHandler<TCommand, TResult>` | Main handler for result command |
| `ICommandPreHandler<TCommand>` / `ICommandValidator<TCommand>` | Pre stage |
| `ICommandPostHandler<TCommand>` / `ICommandPostHandler<TCommand, TResult>` | Post stage |
| `ICommandErrorHandler<TCommand>` / `ICommandErrorHandler<TCommand, TResult>` | Error stage |
| `CommandMediatorExtensions.SendAsync(..., string tag, ...)` | Single-tag routing sugar |

## Packages

- `LiteBus.Commands`
- `LiteBus.Commands.Abstractions`
- `LiteBus.Messaging` (transitive runtime dependency)

## Requires

- `mediator.module-registration`
- `mediator.handler-pipeline`
- `mediator.mediation-settings`

## Invariants

- One main handler must resolve for each command after routing filters.
- `ArgumentNullException` is thrown for `null` commands.
- A gate that stops a result-returning command must supply the result, which `ICommandGate<TCommand, TCommandResult>` makes a compile-time requirement; using the untyped contract instead throws `LiteBusConfigurationException`.
- Post-handler result override applies to result commands only.

## Non-Goals

- Cross-process or distributed command delivery (durable inbox handles deferred execution).
- Exactly-once side effects (mediator axis is in-process invocation).
- Automatic retries or backoff around command handler failures.

## Observability

No dedicated mediator OpenTelemetry meter or activity source exists in the commands axis.

Operational alternatives:
- Use application logging in command handlers and pipeline handlers.
- Use durable axis telemetry when command execution is deferred through inbox processing.
- Use analyzer rules (`LB1001`, `LB1008`) for compile-time command handler shape and coverage.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Send_CreateProductCommand_ShouldGoThroughHandlersCorrectly` | `LiteBus.Mediator.UnitTests` |
| `Send_UpdateProductCommand_ShouldGoThroughHandlersCorrectly` | `LiteBus.Mediator.UnitTests` |
| `Send_LogActivityCommand_ShouldGoThroughHandlersCorrectly` | `LiteBus.Mediator.UnitTests` |
| `mediating_a_command_that_is_short_circuited_by_a_gate_goes_through_correct_handlers` | `LiteBus.Mediator.UnitTests` |
| `Send_CommandWithResult_PostHandlerOverridesResult` | `LiteBus.Mediator.UnitTests` |
| `Send_CommandWithResult_WhenErrorHandlerSetsHandledResult_ShouldReturnFallbackResult` | `LiteBus.Mediator.UnitTests` |
| `Send_Command_WithErrorHandler_ShouldPassTypedContextAndExplicitCancellationToken` | `LiteBus.Mediator.UnitTests` |

### Untested

- Command-specific telemetry emission from mediator axis itself (feature not implemented).
- Large fan-in registration stress for thousands of command handlers in a single module.

### Out-of-Scope

- Durable acceptance semantics (`IInbox.AcceptAsync`) and retry/dead-letter behavior.
- Broker-backed command dispatch and ingress adapters.

## Deep Docs

- [Command module](../../concepts/commands.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Handler filtering](../../concepts/handler-filtering.md)
- [Execution context](../../concepts/execution-context.md)
