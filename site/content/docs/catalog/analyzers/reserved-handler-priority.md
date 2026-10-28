# Reserved Handler Priority

## Header

- **ID**: `analyzers.reserved-handler-priority`
- **Diagnostic**: `LB1022` (Warning)
- **Maturity**: GA
- **Summary**: Reports a `[HandlerPriority]` value inside the range LiteBus reserves for its own handlers, where the handler is ordered against LiteBus stages instead of against other application handlers.

## Trigger Conditions

`LB1022` reports when a handler type carries `[HandlerPriority(int)]` with a value greater than or equal to `HandlerPriorities.ReservedFloor` (`1_000_000`) and less than `HandlerPriorities.ReservedCeiling` (`2_000_000`). A class that implements no handler interface is skipped even when it carries the attribute, because the attribute is inert there.

The floor itself is reported because it is the first reserved value; the ceiling itself is clean because it is the first value above the window.

The rule reads the attribute on one named type at a time and reports on the attribute, so it needs no whole-compilation facts and reports while you type rather than only at build end.

The named LiteBus stages inside the window are `Persistence` (`ReservedFloor + 100`) and `Observability` (`ReservedFloor + 200`); only those two may be reordered relative to each other between releases, while the floor and the ceiling are stable.

Application handlers belong below the floor (an unannotated handler sits at `Default`, which is `0`, before every LiteBus handler) or at or above the ceiling (`UnitOfWork`, which is `ReservedCeiling + 100`, is the commit position for completion handlers).

Reaching for a stage constant such as `HandlerPriorities.Persistence` to order two application handlers against each other compiles and runs, and produces an ordering against the LiteBus stages rather than against the sibling.

## Bad Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

public sealed record ShipOrderCommand : ICommand;

[HandlerPriority(HandlerPriorities.Persistence)]
public sealed class ShipOrderHandler : ICommandHandler<ShipOrderCommand>
{
    public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

Expected diagnostic:

- `LB1022` on the `HandlerPriority` attribute.

## Good Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

public sealed record OrderConfirmedEvent : IEvent;

[HandlerPriority(10)]
public sealed class IssueTickets : IEventHandler<OrderConfirmedEvent>
{
    public Task HandleAsync(OrderConfirmedEvent message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

[HandlerPriority(20)]
public sealed class AdvancePriceTiers : IEventHandler<OrderConfirmedEvent>
{
    public Task HandleAsync(OrderConfirmedEvent message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

Order application handlers against each other with plain values below the floor. Infrastructure that has to run after every LiteBus handler belongs at or above `HandlerPriorities.ReservedCeiling`, with the unit-of-work commit at `HandlerPriorities.UnitOfWork`.

## Suppression Guidance

- Replace the reserved value with a plain value below `ReservedFloor` when the intent is to order application handlers against each other.
- Use a value at or above `ReservedCeiling` when the handler has to run after every LiteBus handler, with the commit itself at `HandlerPriorities.UnitOfWork`.
- Do not suppress to hold a position inside the window: `Persistence` and `Observability` may move relative to each other between releases, so the suppression would pin an ordering LiteBus does not promise.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/ReservedHandlerPriorityAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `HandlerWithNoPriority_ProducesNoDiagnostic` | Handler without the attribute is valid |
| `PriorityBelowTheReservedFloor_ProducesNoDiagnostic` | Value below the floor is valid |
| `PriorityAtTheReservedCeiling_ProducesNoDiagnostic` | `HandlerPriorities.UnitOfWork` is valid |
| `PriorityInsideTheReservedRange_ProducesDiagnostic` | `HandlerPriorities.Persistence` reports `LB1022` |
| `PriorityOnTheReservedFloor_ProducesDiagnostic` | `HandlerPriorities.ReservedFloor` reports `LB1022` |
| `ReservedPriorityOnNonHandler_ProducesNoDiagnostic` | Reserved value on a non-handler type is skipped |
