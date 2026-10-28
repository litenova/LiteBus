# External Side Effect in Command Handler

A command handler runs inside a unit of work that ends at the commit, so a call to the outside world from the handler leaves effects the commit cannot roll back. LB1024 reports command handlers that inject an external side-effect type through their constructor.

## Header

- **ID**: `analyzers.external-side-effect-in-command-handler`
- **Diagnostic**: `LB1024` (Warning, off by default)
- **Maturity**: GA
- **Summary**: Reports command handlers that inject a forbidden outside-world client through their constructor.

## Trigger Conditions

`LB1024` runs on types implementing:

- `ICommandHandler<TCommand>`
- `ICommandHandler<TCommand, TResult>`
- `IAsyncMessageHandler<TMessage>`, `IAsyncMessageHandler<TMessage, TResult>`, or `IMessageHandler<TMessage, TResult>` whose message type is a command

It reports a public constructor parameter whose type is the forbidden type, derives from it, or implements it. Private constructors are skipped, as in the transactional-constructor rules. The default forbidden types are:

- `System.Net.Http.HttpClient`
- `System.Net.Http.IHttpClientFactory`

To replace the defaults, set `litebus.LB1024.forbidden_types` in `.editorconfig` to a comma-separated list of metadata names. A configured value that names nothing reports nothing. Names that do not resolve are ignored. Only constructor parameters are reported.

Enable the rule with `dotnet_diagnostic.LB1024.severity = warning` in `.editorconfig`.

## Bad Example

```csharp
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

public sealed record CreateUserCommand(string Name) : ICommand;

public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    private readonly HttpClient _client;

    public CreateUserCommandHandler(HttpClient client)
    {
        _client = client;
    }

    public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

Expected diagnostic:

- `LB1024` for dependency `System.Net.Http.HttpClient`.

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

Move the outside-world call behind the transactional outbox or into a completion handler that runs after the commit.

## Suppression Guidance

- Prefer moving the call behind the outbox or a completion handler rather than suppressing this warning.
- If a handler must keep a forbidden client during a migration, name the narrower replacement in `litebus.LB1024.forbidden_types` instead of suppressing broadly.
- Keep suppression at handler scope, not project scope.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/ExternalSideEffectInCommandHandlerAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `CommandHandlerWithoutSideEffects_ProducesNoDiagnostic` | Pure command handler is accepted |
| `CommandHandlerInjectingHttpClient_ProducesDiagnostic` | `HttpClient` dependency reports `LB1024` |
| `CommandHandlerInjectingHttpClientFactory_ProducesDiagnostic` | `IHttpClientFactory` dependency reports `LB1024` |
| `CommandHandlerInjectingHttpClientSubclass_ProducesDiagnostic` | `HttpClient` subclass reports `LB1024` |
| `QueryHandlerInjectingHttpClient_ProducesNoDiagnostic` | Query handlers are out of scope |
| `EventHandlerInjectingHttpClient_ProducesNoDiagnostic` | Event handlers are out of scope |
| `MessagingHandlerForCommandInjectingHttpClient_ProducesDiagnostic` | Messaging-level handlers for commands are covered |
| `CommandHandlerWithMethodParameter_ProducesNoDiagnostic` | Non-constructor parameters are out of scope |
| `CommandHandlerWithPrivateSideEffectConstructor_ProducesNoDiagnostic` | Private constructors are out of scope |
| `CustomForbiddenType_ProducesDiagnostic` | Configured forbidden type reports `LB1024` |
| `CustomForbiddenTypeList_ReplacesDefaults` | Configured list replaces the defaults |
| `PerTreeConfiguration_Applies` | Per-file `.editorconfig` section configures the rule |
| `BlankConfiguration_DisablesRule` | Configured value naming nothing reports nothing |
| `Descriptor_IsWarningInHandlersCategoryAndDisabledByDefault` | Rule is a warning in `LiteBus.Handlers`, off by default |
