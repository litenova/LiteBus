# Inbox In-Process Command Dispatch

## Header

- **ID**: `dispatch.inbox.in-process`
- **Name**: Inbox in-process command dispatch
- **Maturity**: GA
- **Summary**: Deserialize leased inbox envelopes and execute them through `ICommandMediator.SendAsync` in the same process as the processor.

## What It Does

`CommandInboxDispatcher` implements `IInboxDispatcher`. It resolves the contract, unprotects and deserializes the payload, verifies the message implements `ICommand`, and sends through the command mediator. Mediation settings mark inbox replay via `InboxExecutionContextKeys.IsInboxExecution` and copy trace metadata from the envelope.

This is the default inbox dispatch path for monolithic services: accept into storage (or via ingress), then run handlers locally when the processor leases the row. No external broker is involved on the dispatch hop.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch.InProcess` | Dispatcher and registration |
| `LiteBus.Commands.Abstractions` | `ICommand`, `ICommandMediator` |
| `LiteBus.Inbox.Abstractions` | `IInboxDispatcher`, envelopes |

## Requires

- `durable-core.inbox`
- `mediator.commands` (`AddCommandModule` before or with inbox)
- `runtime.contract-registry`
- `dispatch.registration`

## Invariants

- Only `ICommand` payloads succeed; other contract types throw `InvalidOperationException`.
- Commands with results (`ICommand<TResult>`) must not be accepted into inbox (LB1004); dispatch assumes fire-and-forget command shape at accept time.
- After-dispatch hook failures default to dead-letter even when dispatch succeeded.
- At-least-once: handler must be idempotent or use accept-time idempotency keys.

## Non-Goals

- Does not publish to external brokers (use transport inbox dispatch).
- Does not dispatch events through `IEventMediator` (planned separately; see Roadmap).
- Does not return command results to the accept caller.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddCommandModule(commands => { /* handlers */ });
    litebus.AddInboxModule(inbox =>
    {
        inbox.EnableInboxProcessor();
        inbox.UseInProcessDispatch();
    });
});
```

### `InboxModuleBuilder.UseInProcessDispatch()`

| | |
| --- | --- |
| Package | `LiteBus.Inbox.Dispatch.InProcess` |
| Registers | `CommandInboxDispatchModule` to `CommandInboxDispatcher` |

### `CommandInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)`

| | |
| --- | --- |
| Package | `LiteBus.Inbox.Dispatch.InProcess` |
| Flow | Resolve contract to unprotect to deserialize to verify `ICommand` to build `CommandMediationSettings` to `ICommandMediator.SendAsync` |
| Mediation items set | `InboxExecutionContextKeys.IsInboxExecution`, `MessageId`, `ContractName`, and trace metadata via `MessageProcessorDiagnostics.ApplyTraceMetadata` |

Optional constructor dependency: `IInboxPayloadProtector` for encrypted payloads.

### `CommandInboxDispatchModule.Build(IModuleConfiguration)`

Registers `IInboxDispatcher` to `CommandInboxDispatcher`. Throws when a second dispatcher is already registered.

## Observability

| Signal | When |
| --- | --- |
| `litebus.inbox.processor.dispatch_duration` | Processor histogram around `DispatchAsync` |
| `litebus.inbox.processor.succeeded` / `failed` / `dead_lettered` | After dispatch and terminal persist |
| Command pipeline tracing | Follows mediator instrumentation when command module registers handlers with diagnostic hooks; no inbox-dispatch-specific activity source |
| No transport send span | In-process path does not touch the transport layer |

Register inbox processor metrics through `AddLiteBusInboxMetrics()`. Command-specific spans depend on application or future mediator OpenTelemetry packages.

## Analyzers

- **LB1004**: Result commands cannot be accepted into inbox.
- **LB1007** / **LB1017**: Contract registration for handled command types.
- **LB1014**: Processor without dispatcher.

## Deep Docs

- [Inbox.md](../../reliable-messaging/inbox.md)
- [Command Inbox Patterns](../durable-core/command-inbox-patterns.md)
- [Command-Module.md](../../concepts/commands.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `DispatchAsync_ShouldExecuteCommandThroughMediator` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `DispatchAsync_WhenMessageIsNotACommand_ShouldThrowInvalidOperationException` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `DispatchAsync_ShouldSetIsInboxExecutionAndCopyTraceMetadata` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `DispatchAsync_WhenCancellationRequested_ShouldPassCancelledTokenToMediator` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `ProcessPendingAsync_ShouldExecuteScheduledCommandThroughPostgreSqlStore` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `ProcessPendingAsync_WhenHandlerFails_ShouldMarkFailedWithVisibleAfter` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `NestedConfiguration_ShouldAcceptAndProcessCommand` | `LiteBus.Inbox.UnitTests` |
| `AddInboxInProcessDispatcher_ShouldRegisterCommandInboxDispatcher` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddInboxInProcessDispatcher_WhenCalledTwice_ShouldThrow` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddInboxInProcessDispatcher_WhenAnotherDispatcherRegistered_ShouldThrow` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |

### Untested

- Full host-level behavior that branches on `InboxExecutionContextKeys.IsInboxExecution`.
- Payload decryption branch with `IInboxPayloadProtector`.

### Out-of-Scope

- Publishing to external brokers (transport inbox dispatch)
- Dispatching events through `IEventMediator` on the inbox axis (planned separately)
- Returning command results to the accept caller
