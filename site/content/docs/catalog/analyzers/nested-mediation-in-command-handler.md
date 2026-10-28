# Nested Mediation in Command Handler

A command handler runs inside a unit of work. LB1023 reports command handlers that send or publish another message through a mediator inside the handle method, which nests a second unit of work inside the first.

## Header

- **ID**: `analyzers.nested-mediation-in-command-handler`
- **Diagnostic**: `LB1023` (Warning, off by default)
- **Maturity**: GA
- **Summary**: Reports command handlers that send, publish, or mediate another message inside `HandleAsync` or `Handle`.

## Trigger Conditions

`LB1023` runs on types implementing:

- `ICommandHandler<TCommand>`
- `ICommandHandler<TCommand, TResult>`
- `IAsyncMessageHandler<TMessage>`, `IAsyncMessageHandler<TMessage, TResult>`, or `IMessageHandler<TMessage, TResult>` whose message type is a command

It reports an invocation inside `HandleAsync` or `Handle` when the call goes through one of the mediators:

- `ICommandMediator.SendAsync` or `TrySendAsync` (including the extension-method overloads)
- `IEventMediator.PublishAsync`
- `IMessageMediator.Mediate`, unless the mediated message is a query

Reads through `IQueryMediator` are not reported. Queries are side-effect free by contract, so nesting a read nests no unit of work. `EvaluateAsync` guard checks are not reported either, because they decide without acting.

Enable the rule with `dotnet_diagnostic.LB1023.severity = warning` in `.editorconfig`.

## Bad Example

```csharp
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
| `Descriptor_IsWarningInHandlersCategoryAndDisabledByDefault` | Rule is a warning in `LiteBus.Handlers`, off by default |
