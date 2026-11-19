# Nested Mediation in Command Handler

A command handler runs inside a unit of work that ends at the commit. LB1023 reports command handlers, pre-handlers, and post-handlers that send or publish another message through a mediator inside the handle method, which nests a second unit of work inside the first.

## Header

- **ID**: `analyzers.nested-mediation-in-command-handler`
- **Diagnostic**: `LB1023` (Warning, reports only when `litebus_forbid_nested_commands` is `true`)
- **Maturity**: GA
- **Summary**: Reports command handlers, pre-handlers, and post-handlers that send, publish, or mediate another message inside `HandleAsync`, `Handle`, `PreHandleAsync`, or `PostHandleAsync`.

## Trigger Conditions

`LB1023` runs on types implementing:

- `ICommandHandler<TCommand>`
- `ICommandHandler<TCommand, TResult>`
- `ICommandPreHandler`, `ICommandPreHandler<TCommand>`
- `ICommandPostHandler`, `ICommandPostHandler<TCommand>`, `ICommandPostHandler<TCommand, TResult>`
- `IAsyncMessageHandler<TMessage>`, `IAsyncMessageHandler<TMessage, TResult>`, or `IMessageHandler<TMessage, TResult>` whose message type is a command
- `IMessagePreHandler<TMessage>` or `IMessagePostHandler<TMessage, TResult>` whose message type is a command

It reports an invocation inside `HandleAsync`, `Handle`, `PreHandleAsync`, or `PostHandleAsync` when the call goes through one of the mediators:

- `ICommandMediator.SendAsync` or `TrySendAsync` (including the extension-method overloads)
- `ICommandBatchMediator.SendManyAsync` or `TrySendManyAsync`
- `IEventMediator.PublishAsync`
- `IMessageMediator.Mediate`, unless the mediated message is a query

Pre-handlers and post-handlers are covered because their `PreHandleAsync` and `PostHandleAsync` methods run inside the same unit of work as the main handler, so a send from any of them nests a second unit of work.

Reads through `IQueryMediator` are not reported. Queries are side-effect free by contract, so nesting a read nests no unit of work. `EvaluateAsync` guard checks are not reported either, because they decide without acting.

The runtime counterpart is `CommandModuleBuilder.ForbidNestedCommands()`, which throws `NestedMediationException` naming both message types before the inner pipeline starts. The analyzer reports the same nesting at build time.

Set `litebus_forbid_nested_commands` to `true` in `.editorconfig` to report this rule at warning. Without the option the rule reports nothing. The analyzer reads both `.editorconfig` and `.globalconfig` entries, and a per-file section overrides the project-wide value.

```ini
# .editorconfig
[*.cs]
litebus_forbid_nested_commands = true
```

An analyzer cannot see a `ForbidNestedCommands()` call made in another compilation, so the option pairs the build check with the runtime guarantee instead of detecting the call. The runtime stays the guarantee.

## Bad Example

```csharp
// The compilation sets litebus_forbid_nested_commands = true.
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

public sealed record CreateUserCommand(string Name) : ICommand;
public sealed record AuditUserCommand(string Name) : ICommand;

public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    private readonly ICommandMediator _commandMediator;

    public CreateUserCommandHandler(ICommandMediator commandMediator)
    {
        _commandMediator = commandMediator;
    }

    public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
        => _commandMediator.SendAsync(new AuditUserCommand(command.Name), cancellationToken: cancellationToken);
}
```

Expected diagnostic:

- `LB1023` for call `LiteBus.Commands.Abstractions.ICommandMediator.SendAsync`.

## Good Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

public sealed record CreateUserCommand(string Name) : ICommand;

public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

When the handler must cause follow-up work, stage that work instead of mediating it inline. Collect domain events and drain them into the transactional outbox after the handler succeeds.

## Suppression Guidance

- Prefer staging follow-up work through the domain event collector or the transactional outbox rather than suppressing this warning.
- If a nested call is required, isolate it in one handler and document why the outer commit must not cover it.
- Keep suppression at handler scope, not project scope.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/NestedMediationInCommandHandlerAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `CommandHandlerWithoutMediation_ProducesNoDiagnostic` | Pure command handler is accepted |
| `CommandHandlerSendingCommand_ProducesDiagnostic` | `ICommandMediator.SendAsync` reports `LB1023` |
| `CommandHandlerSendingCommandThroughExtension_ProducesDiagnostic` | Extension-method overload reports `LB1023` |
| `ResultCommandHandlerSendingCommand_ProducesDiagnostic` | `ICommandHandler<TCommand, TResult>` is covered |
| `CommandHandlerTryingCommand_ProducesDiagnostic` | `ICommandMediator.TrySendAsync` reports `LB1023` |
| `CommandHandlerPublishingEvent_ProducesDiagnostic` | `IEventMediator.PublishAsync` reports `LB1023` |
| `CommandHandlerMediatingMessage_ProducesDiagnostic` | `IMessageMediator.Mediate` reports `LB1023` |
| `CommandHandlerMediatingQuery_ProducesNoDiagnostic` | `Mediate` of a query is treated as a read |
| `CommandHandlerQuerying_ProducesNoDiagnostic` | `IQueryMediator` reads are accepted |
| `CommandHandlerEvaluatingCommand_ProducesNoDiagnostic` | `EvaluateAsync` guard checks are accepted |
| `QueryHandlerSendingCommand_ProducesNoDiagnostic` | Query handlers are out of scope |
| `EventHandlerPublishingEvent_ProducesNoDiagnostic` | Event handlers are out of scope |
| `MessagingHandlerForCommand_ProducesDiagnostic` | Messaging-level handlers for commands are covered |
| `MessagingHandlerForQuery_ProducesNoDiagnostic` | Messaging-level handlers for queries are out of scope |
| `CommandHandlerSendingManyCommands_ProducesDiagnostic` | `ICommandBatchMediator.SendManyAsync` reports `LB1023` |
| `CommandHandlerTryingManyCommands_ProducesDiagnostic` | `ICommandBatchMediator.TrySendManyAsync` reports `LB1023` |
| `BatchSendOutsideCommandHandler_ProducesNoDiagnostic` | `ICommandBatchMediator.SendManyAsync` outside a command handler is accepted |
| `BatchTrySendOutsideCommandHandler_ProducesNoDiagnostic` | `ICommandBatchMediator.TrySendManyAsync` outside a command handler is accepted |
| `CommandPreHandlerSendingCommand_ProducesDiagnostic` | `PreHandleAsync` sending a command reports `LB1023` |
| `CommandPostHandlerSendingCommand_ProducesDiagnostic` | `PostHandleAsync` sending a command reports `LB1023` |
| `MessagingPreHandlerForCommand_ProducesDiagnostic` | Messaging-level pre-handler for a command is covered |
| `MessagingPreHandlerForQuery_ProducesNoDiagnostic` | Messaging-level pre-handler for a query is out of scope |
| `CommandPreHandlerQuerying_ProducesNoDiagnostic` | `IQueryMediator` reads in a pre-handler are accepted |
| `NestingWithoutOption_ProducesNoDiagnostic` | Nesting without the option is accepted |
| `NestingWithOptionFalse_ProducesNoDiagnostic` | Nesting with the option set to `false` is accepted |
| `PerTreeConfiguration_Applies` | A per-file `.editorconfig` section reports `LB1023` |
| `Descriptor_IsWarningInHandlersCategoryAndEnabledByDefault` | Rule is a warning in `LiteBus.Handlers`, enabled by default |
| `Descriptor_DescriptionNamesForbidNestedCommands` | The description names `ForbidNestedCommands()` as the runtime counterpart |
| `Descriptor_DescriptionNamesForbidNestedCommandsOption` | The description names `litebus_forbid_nested_commands` as the reporting option |
