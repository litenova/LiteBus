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

The scope holds no state of its own. It reads and writes the ambient execution context, which flows with the mediation and is discarded when it ends, so two concurrent mediations never see each other. Calling one of its methods outside a mediation raises `NoExecutionContextException`, because there is no record for the value to reach.

`IAuditScope` is registered by `AddMessaging`, not by `EnableAuditing()`, so it resolves whether or not an axis produces records. An application that wants the declaration model and its own writer can inject the scope, read declarations through [`IMessageMetadataAccessor`](message-definitions.md#reading-metadata), and never call `EnableAuditing()`. Nothing reads the scope in that configuration, so what a handler pushes is discarded; that is the intended behavior, not a misconfiguration the framework should report.

Some actions are only accountable with a justification. Declare `ReasonRequired = true` and the writer refuses to produce an incomplete record for a successful action, raising `AuditReasonMissingException` instead. A required justification that silently goes missing defeats the requirement, so it is reported rather than recorded as absent. The throw happens at `HandlerPriorities.Observability`, which is before the commit at `HandlerPriorities.UnitOfWork`, so the work is rolled back rather than standing without its justification. That is the point of the flag; set it only where that trade is the one you want, and call `WithReason` on every path the handler can return through.

## Wiring It Up

Auditing is one decision, made in one place:

```csharp
services.AddLiteBus(registry =>
{
    registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
        .UseTrail<PostgresAuditTrail>()
        .UseActorResolver<RequestActorResolver>()
        .ForCommands()
        .ForQueries()));

    registry.AddCommands(builder => builder.RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly));
    registry.AddQueries(builder => builder.RegisterFromAssembly(typeof(ExportOrdersQuery).Assembly));
});
```

The feature has a shared half and a per-axis half: a trail, an actor resolver and an outcome mapper belong to every axis, and the completion handler that writes a record belongs to each one. `AddAuditing` is what keeps that from being two or three decisions a consumer can make inconsistently. Configuring a trail and selecting no axis fails composition, because no probe can report that at runtime: nothing is ever audited, so nothing ever fails.

The per-axis switches remain and are what `AddAuditing` composes. Reach for them directly only when the axes are genuinely configured in separate places:

```csharp
registry.AddMessaging(messaging => messaging.UseAuditTrail<PostgresAuditTrail>());
registry.AddCommands(builder => builder.EnableAuditing());
registry.AddQueries(builder => builder.EnableAuditing());
registry.AddEvents(builder => builder.EnableAuditing());
```

`UseAuditTrail<T>()` lets the container build the trail, so it may take dependencies of its own, and registers it as **scoped**. That is what a trail wrapping a database session needs, and it is the default for that reason.

Pass a lifetime when you want something else:

```csharp
registry.AddMessaging(messaging => messaging.UseAuditTrail<HttpAuditTrail>(InstanceLifetime.Singleton));
```

`UseAuditTrailInstance(trail)` registers a trail you already hold. A pre-created instance can only be a singleton, so the name says so: a trail built there with a database session captures that one session for the life of the process, and nothing else about the call site would tell you.

The `litebus.audit.trail` probe reports the lifetime it actually observes, as `trailIsSingleton`. It resolves the trail from two dispatch scopes and compares the instances, which is the only way to see the lifetime from outside the container. A singleton trail wrapping a scoped session raises nothing at startup and misbehaves later under load, so the probe names it while the application is still starting.

Registering `IAuditTrail` directly with the application container also works, and the probe accepts either route.

`IAuditTrail` is the only thing you must supply. LiteBus decides when a record is produced and what it contains; where it is written, and with what durability, is your decision.

An application that authorizes by throwing its own exception type registers an outcome mapper beside the trail, through `UseOutcomeMapper<T>()` on the auditing builder, so that its refusal is recorded as `AuditOutcome.Denied` rather than `AuditOutcome.Failed`. An application that denies through a guard or a validator needs no mapper: the pipeline already reports those as decisions, and the code the guard supplied is recorded as the record's `FailureCode`.

### Who Acted

`AuditRecord.Actor` is the first column an audit review reads, and the one part of a record LiteBus cannot derive. It knows the action, the outcome, the target and the clock; who is holding the request lives somewhere different in every application, so supply an `IAuditActorResolver`:

```csharp
internal sealed class RequestActorResolver : IAuditActorResolver
{
    public AuditActor? Resolve(MessageCompletionContext context) => context.Message switch
    {
        IActingAccountCommand acting => AuditActor.User(acting.ActingAccountId.ToString()),
        _ => AuditActor.System(ProcessNameOf(context.Message.GetType()))
    };
}
```

It runs at the completion stage, which is what makes it the right extension point rather than a pre-stage handler. A denied command produces a record, and "who tried" is the most useful thing that record can say, but a pre-handler never runs when a guard denies. Resolving here means attribution survives every path: success, denial, invalid input, failure and cancellation.

`AuditActor` carries a required `Id` and an optional `Kind`, `DisplayName` and `OnBehalfOf`. `Kind` exists so a query can separate the actions people took from the actions a process took, which is a distinction every review draws and an identifier alone cannot express. `OnBehalfOf` is for a delegated action, which is what separates support staff acting as a customer from the customer acting, and a device acting on a key from the person who authorized that key.

Returning `null` is legitimate and means nothing established an actor. Do not invent one: a fabricated identifier in evidence is worse than a gap a review can see. Where the application knows a process acted, say so with `AuditActor.System`, because a scheduled job and an unattributed action are different answers.

`IAuditScope.WithActor` overrides the resolver for the case only the handler knows, such as an actor established by exchanging a token mid-handler. Attribution that is the same for every message belongs in the resolver, which also covers the paths a handler never reaches.

Without a resolver, records are still written and every one has no actor. The `litebus.audit.trail` probe reports that as `Degraded` rather than unhealthy: a trail that says what happened is worth writing, it just cannot say who is answerable, and that is not something to discover during a review.

### Auditing Events

`EnableAuditing` exists on the event module too. A domain fact is frequently the thing a review most wants recorded, and it is not always the consequence of an audited command: an event raised by an inbox replay, a scheduled reconciliation, or an integration from another system has no command behind it.

One record per publish, not per handler. The mediation is the unit being audited and the broadcast strategy reports one outcome for the whole publish, so a record per subscriber would multiply one fact into as many entries as there happen to be reactions, and would change count whenever a handler is added.

Auditing both a command and the event it raises does record the same business change twice, from two angles: the action somebody took and the fact it produced. Decide which you want and declare an audit position on that one. Declaring both is a choice rather than a mistake, and the action codes keep them apart.

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
| `Actor` | Who did it, from the actor resolver or the audit scope |
| `Outcome` | `Succeeded`, `Denied`, `Invalid`, `Failed`, or `Canceled` (an early answer is a success) |
| `OccurredAt`, `Duration` | When it happened and how long it took |
| `Category` | Grouping that drives review and retention |
| `TargetKind`, `TargetId` | What was acted on |
| `Reason` | Why, where the action requires one |
| `FailureCode` | Stable code for a non-success: the code a guard or validator supplied, otherwise the exception type name |
| `MessageType` | For correlating with the diagnostic log |
| `CorrelationId`, `TenantId` | Read from the execution context when present |
| `Properties` | Non-identifying context attached by the handler |

Note what is deliberately absent: the message itself, and any before-and-after snapshot of the changed state. The message is handed to the actor resolver instead, so a payload cannot reach audit storage by default. Handing it to the trail would put the whole payload in front of every trail implementation and make keeping personal data out of audit storage the implementer's discipline rather than the framework's. That is the field which turns an audit table into an erasure liability under data-protection law, and it is redundant, because the domain event stream already records what changed under its own retention rule. The trail records who is answerable and why.

For the same reason, do not put personal data in `Properties`. A trail that holds pseudonymous identifiers can serve an erasure request by breaking the identity mapping, which leaves the records meaningful and the person unidentifiable. A trail full of names cannot.

## Building the Catalogue From the Declarations

Which actions does this system audit, under what category, and which of them require a justification? That is a compliance artifact many teams maintain by hand and keep wrong, and it is a pure function of what the messages declare:

```csharp
var catalog = provider.GetRequiredService<IMessageCatalog>();

foreach (var row in catalog.ToRows())
{
    Console.WriteLine($"{row.Action} ({row.Category}) on {row.TargetKind}");
}

File.WriteAllText("audit-catalogue.md", catalog.ToMarkdown());
```

`IMessageCatalog` resolves at runtime as well as inside a composition check. `ToRows` gives an `AuditCatalogueRow` per audited message, ordered by action so two runs produce the same document, and `ToMarkdown` is one formatter over those rows. Rows are the primary surface because what a compliance process consumes differs per team: a wiki page for one, a spreadsheet attached to an audit for another. A library that emitted only Markdown would serve the first and obstruct the second.

An exempt or undeclared message is absent, because a catalogue of audited actions is what this builds. Enumerate the catalog itself and read `DeclarationExemptions` to report the exemptions and their rationales, which answers a different question and is worth its own document.

The other half of an authorization matrix stays yours. A required permission is your value type, so project it from the resolved metadata alongside these rows:

```csharp
var matrix = catalog
    .Where(entry => entry.Metadata.TryGet<RequiredPermission>(out _))
    .Select(entry =>
    {
        entry.Metadata.TryGet<RequiredPermission>(out var permission);
        return new { entry.MessageType.Name, Permission = permission!.Name, entry.Audit?.Action };
    });
```

LiteBus applies your declarations without understanding them, so it can hand them back but cannot name their columns for you.

## Making a Record Atomic With Its Change

An audit record that can exist without the change it describes, or a change that can exist without its record, is worth less than either. Making the two atomic means the record has to be written by the same transaction that writes the change, and that constrains where your commit goes.

Put the commit in a **completion handler** at `HandlerPriorities.UnitOfWork`:

```csharp
[HandlerPriority(HandlerPriorities.UnitOfWork)]
public sealed class CommitUnitOfWork : ICommandCompletionHandler
{
    private readonly IDocumentSession _session;

    public CommitUnitOfWork(IDocumentSession session) => _session = session;

    public async Task HandleCompletionAsync(
        MessageCompletionContext<ICommand> context,
        CancellationToken cancellationToken)
    {
        if (context.Outcome is MediationOutcome.Succeeded or MediationOutcome.Answered)
        {
            await _session.SaveChangesAsync(CancellationToken.None);
        }
    }
}
```

Your `IAuditTrail` then stages the record instead of writing it:

```csharp
public sealed class MartenAuditTrail : IAuditTrail
{
    private readonly IDocumentSession _session;
    private readonly IAuditArchive _archive;

    public MartenAuditTrail(IDocumentSession session, IAuditArchive archive)
    {
        _session = session;
        _archive = archive;
    }

    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        if (record.Outcome == AuditOutcome.Succeeded)
        {
            // Staged, not written. The commit below flushes it with the change.
            _session.Store(record);
            return Task.CompletedTask;
        }

        // The transaction carrying the change is being rolled back, so this record cannot ride it.
        return _archive.WriteAsync(record, cancellationToken);
    }
}
```

Three things make this work, and all three are guarantees rather than incidental behavior:

- The audit writer runs at `HandlerPriorities.Observability`, inside the reserved window. `HandlerPriorities.UnitOfWork` sits above `ReservedCeiling`, so the commit runs after the writer in this and every future release. See [Handler Priority](handler-priority.md#the-reserved-framework-window).
- The completion stage orders by priority alone, unlike every other role. A commit registered broadly for `ICommand` still runs after a framework writer registered the same way, and after a handler registered for one concrete command.
- A completion handler that throws on an otherwise clean mediation propagates its exception to the caller. A commit that hits a concurrency conflict is reported, not swallowed. When the mediation had already failed, the fault is attached to the original exception under `LiteBus.SuppressedCompletionFaults` instead of replacing it.

The commit belongs in the completion stage rather than a post-handler for a reason worth stating plainly: a post-handler never runs when the main handler throws, so a commit placed there cannot decide anything about a failure, and everything LiteBus writes afterwards is outside the transaction by construction. The completion stage is the first point that sees how the mediation actually ended.

Two consequences to plan for. A record for a refusal or a failure has to be written out of band, as above, and that write has to survive whatever caused the failure. And anything your commit handler does after the commit, such as dispatching the domain events the aggregates recorded, runs with `CancellationToken.None`, because the completion stage is not cancellable.

## What LiteBus Deliberately Leaves to You

- **Where records are stored, and their integrity.** A hash chain, an append-only database role, and a retention job are all worth having, and all belong in your `IAuditTrail` implementation rather than in a messaging library.
- **Where the transaction boundary is.** LiteBus does not open, commit, or roll back anything, because only your application knows what its unit of work contains. It does guarantee a position you can commit from, described under [Making a Record Atomic With Its Change](#making-a-record-atomic-with-its-change) below.
- **The actor.** LiteBus does not model identity. Capture the acting account in your trail implementation, from whatever ambient principal your host provides.

## Next

Read [Message Definitions](message-definitions.md) for the declaration mechanism, and [The Handler Pipeline](handler-pipeline.md) for the completion stage this is built on. [In-Process Idempotency](idempotency.md) uses the same declaration model and the same commit position, so the two configure together.
