# Subscriber Names

Per-handler outbox delivery records a stable subscriber name for each event handler instead of the handler's full type name, so renaming a class does not orphan the envelopes already written. `LB1027` warns when one subscriber name is shared by handlers of different events, which makes outbox rows hard to read in operations tooling and makes handler renames error-prone.

## Header

- **ID**: `analyzers.subscriber-name-reused`
- **Diagnostic**: `LB1027` (Warning)
- **Maturity**: GA
- **Summary**: Reports one subscriber name shared by handlers of different message types.

## Trigger Conditions

`LB1027` reports when:

- Two or more handler types declared in the analyzed assembly carry `[SubscriberName]` with the same value.
- The handlers handle different message types.

Names compare ordinally, matching the dispatch-time match in `SubscriberNames.Matches`. Attribute values may use string literals or `const` values; the analyzer resolves constant expressions to their values before comparison. A partial handler counts once however many declarations it has.

Two handlers of one event sharing a name are not reported here: composition validation already refuses that duplicate with `LiteBusConfigurationException` when per-handler delivery is on. This rule covers the reuse composition allows, where handlers of different messages share one name.

The rule analyzes one compilation. Only handlers declared in the analyzed assembly are candidates, and a handler that lives in a dependent project stays invisible, so co-locate the handlers or suppress the diagnostic. One diagnostic is reported per reused name, at the second handler's declaration, naming the pair that introduces the second message type. See [Analyzers](../../reference/analyzers.md).

## Naming Pattern

Each module owns the subscriber names under its own prefix. Name a subscriber `<module>.<event>.<reaction>` in kebab-case segments matching the contract name prefix, naming the reaction rather than the class:

- `ledger.entry-posted.cash-projection` for the handler that projects posted ledger entries into the cash balance.
- `ledger.entry-posted.fraud-screening` for the handler that screens the same event for fraud.

The module segment keeps names unique across modules, the event segment ties the row to the fact that caused it, and the reaction segment tells operations what the row does. Do not record the class name (`UpdateCashProjection`): it changes on every rename, which is what subscriber names exist to survive. Do not shorten the name to the reaction alone (`cash.projection`) once more than one module projects cash: the short name reads fine until the second module arrives, and then two reactions share it.

## Retiring a Name

Dispatch matches a recorded value against both the subscriber name and the handler's full type name, so a name can be retired without losing envelopes:

1. Add the new name to the handler and deploy.
2. Let the envelopes recorded under the old name drain through the processor.
3. Remove the old handler registration once no row carries the old name.

Never reuse a retired name for a different reaction: rows recorded under it would then address the new reaction.

## Bad Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

[SubscriberName("cash.projection")]
public sealed class CashProjectionHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

[SubscriberName("cash.projection")]
public sealed class RefundProjectionHandler : IEventHandler<OrderRefundedEvent>
{
    public Task HandleAsync(OrderRefundedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed record OrderCreatedEvent : IEvent;

public sealed record OrderRefundedEvent : IEvent;
```

Expected diagnostic:

- `LB1027` for name `cash.projection`, used by `CashProjectionHandler` and by `RefundProjectionHandler`.

## Good Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

[SubscriberName("ledger.order-created.cash-projection")]
public sealed class CashProjectionHandler : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

[SubscriberName("ledger.order-refunded.cash-projection")]
public sealed class RefundProjectionHandler : IEventHandler<OrderRefundedEvent>
{
    public Task HandleAsync(OrderRefundedEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed record OrderCreatedEvent : IEvent;

public sealed record OrderRefundedEvent : IEvent;
```

Each reaction has its own name, so outbox rows name the reaction that produced them.

## Suppression Guidance

- Give each reaction its own name instead of suppressing. The warning means two rows that read identically do different work.
- Suppress only while a rename is mid-flight, with the new name already decided and the old one draining.
- Keep suppression at handler scope, with a comment naming the module that owns the shared name.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/SubscriberNameReusedAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `SharedNameAcrossDifferentEvents_ProducesDiagnostic` | Handlers of different events sharing one name report `LB1027` |
| `SharedNameForSameEvent_ProducesNoDiagnostic` | Same-event duplicates are left to composition validation |
| `DistinctNames_ProducesNoDiagnostic` | Distinct names satisfy the rule |
| `SharedNameThroughConst_ProducesDiagnostic` | `const` subscriber name values are resolved before comparison |
| `SubscriberNameOnNonHandler_ProducesNoDiagnostic` | The attribute on a non-handler type is ignored |
| `ThirdHandlerIntroducingSecondMessage_NamesFirstAndThird` | The diagnostic names the pair introducing the second message type |
| `Descriptor_IsWarningInOutboxCategoryAndEnabledByDefault` | Rule is a warning in `LiteBus.Outbox`, on by default |
