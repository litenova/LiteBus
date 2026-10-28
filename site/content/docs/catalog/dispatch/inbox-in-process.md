# Inbox In-Process Command Dispatch

## Header

- **ID**: `dispatch.inbox.in-process`
- **Name**: Inbox in-process command dispatch
- **Maturity**: GA
- **Summary**: Deserialize leased inbox envelopes and execute them through `ICommandMediator.SendAsync` in the same process as the processor.

## What It Does

`CommandInboxDispatcher` implements `IInboxDispatcher`. It resolves the contract, unprotects and deserializes the payload, verifies the message implements `ICommand`, and sends the message through the command mediator. The mediation settings record the inbox replay in `InboxExecutionContextKeys.IsInboxExecution` and copy trace metadata from the envelope.

This is the default inbox dispatch path for monolithic services. The application accepts messages into storage, directly or through ingress. Handlers then run locally when the processor leases the row. No external broker is involved on the dispatch hop.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch.InProcess` | Dispatcher and registration |
| `LiteBus.Commands.Abstractions` | `ICommand`, `ICommandMediator` |
| `LiteBus.Inbox.Abstractions` | `IInboxDispatcher`, envelopes |

## Requires

- `durable-core.inbox`
- `mediator.commands` (`AddCommands` before or with inbox)
- `runtime.contract-registry`
- `dispatch.registration`

## Invariants

- Only `ICommand` payloads succeed. Other contract types throw `InvalidOperationException`.
- Commands with results (`ICommand<TResult>`) must not be accepted into the inbox (LB1004). Dispatch assumes the fire-and-forget command shape at accept time.
- By default, after-dispatch hook failures dead-letter the envelope even when dispatch succeeded.
- Delivery is at-least-once, so a handler must be idempotent or use accept-time idempotency keys.

## Non-Goals

- It does not publish to external brokers. Use transport inbox dispatch for that.
- It does not dispatch events through `IEventMediator`. That is planned separately (see Roadmap).
- It does not return command results to the accept caller.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddCommands(commands => { /* handlers */ });
    litebus.AddInbox(inbox =>
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
| Flow | Resolves the contract, unprotects and deserializes the payload, verifies `ICommand`, builds `CommandMediationSettings`, and calls `ICommandMediator.SendAsync` |
| Mediation items set | `InboxExecutionContextKeys.IsInboxExecution`, `MessageId`, `ContractName`, and trace metadata copied through `MessageProcessorDiagnostics.ApplyTraceMetadata` |

An optional constructor dependency, `IInboxPayloadProtector`, unprotects encrypted payloads.

### `CommandInboxDispatchModule.Build(IModuleConfiguration)`

The method registers `IInboxDispatcher` to `CommandInboxDispatcher`. Registering a second dispatcher on the same builder throws `DurableStorageConfigurationException` from `InboxModuleBuilder.RegisterDispatcher`.

## Observability

| Signal | When |
| --- | --- |
| `litebus.inbox.processor.dispatch_duration` | A processor histogram around `DispatchAsync` |
| `litebus.inbox.processor.succeeded` / `failed` / `dead_lettered` | After dispatch and the terminal persist |
| Command pipeline tracing | It follows mediator instrumentation when the command module registers handlers with diagnostic hooks. There is no inbox-dispatch-specific activity source |
| No transport send span | The in-process path does not touch the transport layer |

Register inbox processor metrics through `AddLiteBusInboxMetrics()`. Command-specific spans depend on the application or on future mediator OpenTelemetry packages.

## Analyzers

- **LB1004**: Result commands cannot be accepted into the inbox.
- **LB1007** / **LB1017**: Contract registration for handled command types.
- **LB1014**: A processor without a dispatcher.

## References

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
| `DispatchAsync_WhenCancellationRequested_ShouldStopBeforeDeserialization` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `ProcessPendingAsync_ShouldExecuteScheduledCommandThroughPostgreSqlStore` | `LiteBus.Storage.IntegrationTests` (`PostgreSql/`) |
| `ProcessPendingAsync_WhenHandlerFails_ShouldMarkFailedWithVisibleAfter` | `LiteBus.Storage.IntegrationTests` (`PostgreSql/`) |
| `NestedConfiguration_ShouldAcceptAndProcessCommand` | `LiteBus.Inbox.UnitTests` |
| `AddInboxInProcessDispatcher_ShouldRegisterCommandInboxDispatcher` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddInboxInProcessDispatcher_WhenCalledTwice_ShouldThrow` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddInboxInProcessDispatcher_WhenAnotherDispatcherRegistered_ShouldThrow` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |

### Untested

- Full host-level behavior that branches on `InboxExecutionContextKeys.IsInboxExecution`.
- Payload decryption branch with `IInboxPayloadProtector`.

### Out-of-Scope

- Publishing to external brokers, which transport inbox dispatch covers
- Dispatching events through `IEventMediator` on the inbox axis, which is planned separately
- Returning command results to the accept caller
