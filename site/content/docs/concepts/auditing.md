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

Writing audit records inside a handler can only ever record success. Authorization typically throws or aborts before the audit line is reached, and a post-handler does not run when the main handler throws. A trail of successes is a changelog, not an audit.

LiteBus writes at the [completion stage](handler-pipeline.md), which runs on every path: success, refusal, failure, and cancellation. Recording the ending becomes structural rather than a rule people have to remember.

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

        _audit.Target(order.Id.ToString())
              .WithReason("customer requested")
              .WithProperty("channel", "web");

        return Task.FromResult(order.Id);
    }
}
```

The scope holds no state of its own. It reads and writes the ambient execution context, which flows with the mediation and is discarded when it ends, so two concurrent mediations never see each other.

## Wiring It Up

Provide a trail and turn auditing on per axis:

```csharp
services.AddSingleton<IAuditTrail, PostgresAuditTrail>();

services.AddLiteBus(registry =>
{
    registry.AddMessaging(_ => { });

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

`IAuditTrail` is the only thing you must supply. LiteBus decides when a record is produced and what it contains; where it is written, and with what durability, is your decision.

## Recording Refusals as Denials

LiteBus knows that a mediation failed. It cannot know whether it failed **because the actor was not permitted**, which is the distinction a security review cares about most, because the exception that carries it belongs to your application.

By default an aborted mediation is recorded as `AuditOutcome.Denied`, since aborting is how a pre-handler refuses to let a message proceed, and every other failure is recorded as `AuditOutcome.Failed`. If you refuse by throwing, register a mapper:

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

## The Record

`AuditRecord` follows the model that NIST SP 800-53 AU-3, PCI DSS Requirement 10, and the DMTF CADF event model all describe: an initiator performs an action on a target, producing an outcome, observed at a time and from a place. Building to that shape costs nothing and lets the trail map onto a SIEM schema later without being remodelled.

| Field | Holds |
| --- | --- |
| `Action` | Use-case identity, such as `orders.place-order` |
| `Outcome` | `Succeeded`, `Denied`, `Failed`, or `Canceled` |
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
- **Transaction behavior.** A record for a successful action should be written in the same transaction as the change it describes, so an action cannot exist without its record. A record for a refusal cannot ride that transaction, because the transaction is the one being rolled back; it has to be written out of band and must survive the failure that caused it.
- **The actor.** LiteBus does not model identity. Capture the acting account in your trail implementation, from whatever ambient principal your host provides.

## Next

Read [Message Definitions](message-definitions.md) for the declaration mechanism, and [The Handler Pipeline](handler-pipeline.md) for the completion stage this is built on.
