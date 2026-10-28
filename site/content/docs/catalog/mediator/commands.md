# Commands

- **ID**: `mediator.commands`
- **Name**: Commands
- **Maturity**: GA
- **Summary**: Executes state-changing messages through exactly one main handler, plus decision, pre, post, error, and completion stages.
- **Guide**: [Command module](../../concepts/commands.md)

## What It Does

`ICommandMediator` sends command messages through `SingleAsyncHandlerMediationStrategy`. Commands resolve one main handler after routing filters are applied. Direct handlers win, and an indirect (base-type) handler applies only when no direct handler survives. Mediation throws `NoHandlerFoundException` when no handler survives filtering, and `MultipleHandlerFoundException` when more than one handler at the winning level (direct, else indirect) survives. Both exceptions name the runtime message type, even when a non-generic overload erased it to `ICommand`.

Commands support both void (`ICommand`) and result (`ICommand<TResult>`) flows. A guard, a validator, or a shortcut can stop execution before the handler runs, with a refusal, a report that the message is malformed, or an early answer. Post-handlers can observe results, and post-handlers on result commands can replace the returned value through `AmbientExecutionContext.Current.MessageResult`.

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
| `ICommandMediator.TrySendAsync` / `ICommandMediator.EvaluateAsync` | Returns a refusal as a `MediationResult` value, and evaluates guards and validators as a `MediationDecision` |
| `ICommandHandler<TCommand>` | Main handler for void command |
| `ICommandHandler<TCommand, TResult>` | Main handler for result command |
| `ICommandGuard<TCommand>` / `ICommandValidator<TCommand>` / `ICommandShortcut<TCommand>` / `ICommandShortcut<TCommand, TCommandResult>` / `ICommandPreHandler<TCommand>` | Decision and pre stages, in framework-fixed order |
| `ICommandRefusalMapper<TCommand, TCommandResult>` | Turns a refusal into the value a result-command caller receives |
| `ICommandPostHandler<TCommand>` / `ICommandPostHandler<TCommand, TResult>` | Post stage |
| `ICommandErrorHandler<TCommand>` / `ICommandErrorHandler<TCommand, TResult>` | Error stage |
| `ICommandCompletionHandler` / `ICommandCompletionHandler<TCommand>` / `ICommandCompletionHandler<TCommand, TCommandResult>` | Completion stage, on every outcome path |
| `ThrowingCommandValidator<TCommand, TException>` | Migration adapter for a validator whose body still reports failure by throwing |
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

- One main handler must resolve for each command after routing filters, at the winning level (direct, else indirect).
- `ArgumentNullException` is thrown for `null` commands.
- A shortcut that answers a result-returning command must supply the result, which `ICommandShortcut<TCommand, TCommandResult>` makes a compile-time requirement. Using the untyped contract instead throws `LiteBusConfigurationException`. A guard carries no such duty, so `ICommandGuard<TCommand>` fits every command.
- Post-handler result override applies to result commands only.

## Non-Goals

- Cross-process or distributed command delivery (durable inbox handles deferred execution).
- Exactly-once side effects (mediator axis is in-process invocation).
- Automatic retries or backoff around command handler failures.

## Observability

Command mediation is covered by the shared `LiteBus.Mediation` activity source and meter, which record one span per mediation plus duration, count, and decision instruments. Per-stage spans and metrics are opt-in through `MessageModuleBuilder.UseTelemetry`. See [Handler pipeline](handler-pipeline.md) for the instrument table.

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
| `mediating_a_command_answered_by_a_shortcut_goes_through_correct_handlers` | `LiteBus.Mediator.UnitTests` |
| `Send_CommandWithResult_PostHandlerOverridesResult` | `LiteBus.Mediator.UnitTests` |
| `Send_CommandWithResult_WhenErrorHandlerSetsHandledResult_ShouldReturnFallbackResult` | `LiteBus.Mediator.UnitTests` |
| `Send_Command_WithErrorHandler_ShouldPassTypedContextAndExplicitCancellationToken` | `LiteBus.Mediator.UnitTests` |

### Untested

- Per-stage span and metric emission for command mediation under the opt-in telemetry flags.
- Large fan-in registration stress for thousands of command handlers in a single module.

### Out-of-Scope

- Durable acceptance semantics (`IInbox.AcceptAsync`) and retry/dead-letter behavior.
- Broker-backed command dispatch and ingress adapters.

## References

- [Command module](../../concepts/commands.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Handler filtering](../../concepts/handler-filtering.md)
- [Execution context](../../concepts/execution-context.md)
