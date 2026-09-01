# Auditing

An audit trail answers a question no other log answers: for any action a person or a machine took inside somebody else's data, who did it, what they did it to, when, why, and whether it worked. LiteBus produces those records at the mediation boundary, from metadata you declare once per message, on every outcome including refusals. This page explains the model, the wiring, and the decisions LiteBus deliberately leaves to you.

## Three Logs That Are Not the Same Log

Before wiring anything, it helps to separate three things that are often confused:

| Log | Records | Read by |
| --- | --- | --- |
| Diagnostic log | What the software did | Engineers, during an incident |
| Domain event stream | What became true | The system itself |
| Audit trail | Who is answerable | Auditors, security reviewers, customers |

The tempting shortcut is to derive the third from the second. It does not hold up. A domain event carries domain identity rather than use-case identity, and the mapping is not one to one. It carries only what the domain needed, so it may omit the device that acted. Most decisively, a domain event only exists when state changed, and a refused action changes nothing, yet refusals are precisely what an audit is asked about.

Keep the streams separate. LiteBus does not publish audit records through the outbox for the same reason: the outbox gives at-least-once delivery to other systems, while evidence needs durability at the source.

## Why the Boundary and Not the Handler

Writing audit records inside a handler can only ever record success. Authorization typically refuses before the audit line is reached, and a post-handler does not run when the main handler throws. A trail of successes is a changelog, not an audit.

LiteBus writes at the [completion stage](handler-pipeline.md), which runs on every path: success, early answer, refusal, failure, and cancellation. Recording the ending becomes structural rather than a rule people have to remember. That stage is also not cancellable, which is what makes a cancelled mediation leave a record rather than dropping the one entry a review would look for.

## Declaring What Is Audited

Every audited message declares the constant half of its record: the parts known without running anything.

With an attribute, when the declaration is a single fact:

```csharp
[Audited("orders.place-order", Category = "money", TargetKind = "order")]
public sealed record PlaceOrderCommand(Guid CartId) : ICommand<OrderId>;
```

With a [definition](message-definitions.md), when you want real types and shared constants:

```csharp
public sealed class PlaceOrderCommandDefinition : IAuditDefinition<PlaceOrderCommand>
{
    public AuditDeclaration Audit => AuditDeclaration.Audited(AuditActions.Orders.Place) with
    {
        Category = AuditCategories.Money,
        TargetKind = "order"
    };
}
```

A definition wins when both are present.

Messages that are not audited say so, and say why:

```csharp
[AuditExempt("browsing a public storefront is not a sensitive action")]
public sealed record GetStorefrontQuery(Guid StoreId) : IQuery<StorefrontView>;
```

An exemption is a decision, not an omission. Recording the rationale beside the message is what makes the selection of audited events reviewable, and it is what auditing standards ask for when they require event selection to be documented along with its justification. It also keeps that rationale from drifting away from the code, the way a separate document does.

The action code is **use-case identity**, not domain identity. Two call sites that raise the same domain event are two different actions if a person would describe them differently.

## Supplying What Only the Handler Knows

The variable half of a record is known only while the handler runs. A command that creates a resource generates its identifier internally, and a reason composed at runtime cannot be declared in advance. Resolve `IAuditScope` and push what you alone know:

```csharp
public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, OrderId>
{
    private readonly IAuditScope _audit;

    public PlaceOrderCommandHandler(IAuditScope audit) => _audit = audit;

    public Task<OrderId> HandleAsync(PlaceOrderCommand message, CancellationToken cancellationToken = default)
    {
        var order = Order.Place(message.CartId);

        _audit.WithTarget(order.Id.ToString())
              .WithReason("customer requested")
              .WithProperty("channel", "web");

        return Task.FromResult(order.Id);
    }
}
```

The scope holds no state of its own. It reads and writes the ambient execution context, which flows with the mediation and is discarded when it ends, so two concurrent mediations never see each other.

Some actions are only accountable with a justification. Declare `ReasonRequired = true` and the writer refuses to produce an incomplete record for a successful action, raising `LiteBusConfigurationException` instead. A required justification that silently goes missing defeats the requirement, so it is reported rather than recorded as absent.

## Wiring It Up

Auditing is configured in two halves, and the split follows the two questions it answers. The trail and the outcome mapper are shared by every axis, so they go on the messaging module. Whether a given axis produces records at all is a per-axis decision, so it goes on that axis:

```csharp
services.AddLiteBus(registry =>
{
    registry.AddMessaging(messaging => messaging.UseAuditTrail<PostgresAuditTrail>());

    registry.AddCommands(builder =>
    {
        builder.RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly);
        builder.EnableAuditing();
    });

    registry.AddQueries(builder =>
    {
        builder.RegisterFromAssembly(typeof(ExportOrdersQuery).Assembly);
        builder.EnableAuditing();
    });
});
```

`UseAuditTrail<T>()` lets the container build the trail, so it may take dependencies of its own. `UseAuditTrail(instance)` registers one you already hold. Registering `IAuditTrail` directly with the application container also works, and the `litebus.audit.trail` diagnostic check accepts either.

`IAuditTrail` is the only thing you must supply. LiteBus decides when a record is produced and what it contains; where it is written, and with what durability, is your decision.

An application that authorizes by throwing its own exception type registers `UseAuditOutcomeMapper<T>()` beside the trail, so that its refusal is recorded as `AuditOutcome.Denied` rather than `AuditOutcome.Failed`. An application that denies through a guard or a validator needs no mapper: the pipeline already reports those as decisions.

### Events Are Not Audited

`EnableAuditing` exists on the command and query modules and not on the event module. An audit trail records who attempted an action and how it ended, and an event is not an attempt: it is a fact that has already happened, published by a handler whose own command was audited. Auditing events would record the same action twice, once as the decision and once as its consequence, and only the first is answerable.

Record the event stream where it belongs, in the domain event log, and keep the trail for the actions people take. An event handler that performs an audited action of its own sends a command for it, and that command is audited normally.

Enabling auditing also registers the `litebus.audit.trail` diagnostic probe, which reports unhealthy when no trail is registered. Without it, a missing trail first shows up as a fault inside the completion stage, which is the one stage whose faults are deliberately kept away from the caller.

## Denials and Early Answers Are Not the Same Ending

The distinction a security review cares about most is whether the actor was permitted. The pipeline carries it: a [guard](handler-pipeline.md) that returns `Deny` reports `MediationOutcome.Denied` and is recorded as `AuditOutcome.Denied`, with the reason the guard gave.

A shortcut that answers is a different event. A cache hit or a replayed idempotent command refused nobody, so it is recorded as `AuditOutcome.Succeeded`. Recording it as a denial would put an entry in the list a reviewer reads that never happened. A validation failure is a third event: the message was malformed rather than refused, so it is recorded as `AuditOutcome.Invalid` and stays out of the denial list too.

| Ending | `MediationOutcome` | Recorded as |
| --- | --- | --- |
| The handler ran and post-handlers completed | `Succeeded` | `Succeeded` |
| A shortcut answered without the handler | `Answered` | `Succeeded` |
| A validator reported the message malformed | `Invalid` | `Invalid` |
| A guard refused the message | `Denied` | `Denied` |
| The pipeline threw | `Failed` | `Failed` |
| The caller cancelled | `Canceled` | `Canceled` |

An application that refuses by **throwing** rather than through a guard owns the exception type, so LiteBus cannot classify it. Register a mapper to have that exception recorded as a denial:

```csharp
public sealed class UseCaseAuditOutcomeMapper : IAuditOutcomeMapper
{
    public AuditOutcome Map(MessageCompletionContext context) => context.Exception switch
    {
        ForbiddenException => AuditOutcome.Denied,
        _ => DefaultAuditOutcomeMapper.MapByOutcome(context)
    };
}

registry.AddMessaging(messaging => messaging.UseAuditOutcomeMapper(new UseCaseAuditOutcomeMapper()));
```

Refusing through a guard or a validator needs no mapper.

## The Record

`AuditRecord` follows the model that NIST SP 800-53 AU-3, PCI DSS Requirement 10, and the DMTF CADF event model all describe: an initiator performs an action on a target, producing an outcome, observed at a time and from a place. Building to that shape costs nothing and lets the trail map onto a SIEM schema later without being remodelled.

| Field | Holds |
| --- | --- |
| `Action` | Use-case identity, such as `orders.place-order` |
| `Outcome` | `Succeeded`, `Denied`, `Failed`, or `Canceled` (an early answer is a success) |
| `OccurredAt`, `Duration` | When it happened and how long it took |
| `Category` | Grouping that drives review and retention |
| `TargetKind`, `TargetId` | What was acted on |
| `Reason` | Why, where the action requires one |
| `FailureCode` | Stable code for a non-success, defaulting to the exception type name |
| `MessageType` | For correlating with the diagnostic log |
| `CorrelationId`, `TenantId` | Read from the execution context when present |
| `Properties` | Non-identifying context attached by the handler |

Note what is deliberately absent: any before-and-after snapshot of the changed state. That is the field which turns an audit table into an erasure liability under data-protection law, and it is redundant, because the domain event stream already records what changed under its own retention rule. The trail records who is answerable and why.

For the same reason, do not put personal data in `Properties`. A trail that holds pseudonymous identifiers can serve an erasure request by breaking the identity mapping, which leaves the records meaningful and the person unidentifiable. A trail full of names cannot.

## What LiteBus Deliberately Leaves to You

- **Where records are stored, and their integrity.** A hash chain, an append-only database role, and a retention job are all worth having, and all belong in your `IAuditTrail` implementation rather than in a messaging library.
- **Transaction behavior.** A record for a successful action is best written in the same transaction as the change it describes, so an action cannot exist without its record. The record arrives at the completion stage, which runs after post-handlers, so a unit of work opened inside the pipeline has usually committed by then. To share the transaction, buffer the record in the unit of work and let the commit flush it; LiteBus does not buffer on your behalf, because only your application knows where its transaction boundary is. A record for a refusal or a failure cannot ride that transaction in any case, because the transaction is the one being rolled back; it has to be written out of band and must survive the failure that caused it.
- **The actor.** LiteBus does not model identity. Capture the acting account in your trail implementation, from whatever ambient principal your host provides.

## Next

Read [Message Definitions](message-definitions.md) for the declaration mechanism, and [The Handler Pipeline](handler-pipeline.md) for the completion stage this is built on.
