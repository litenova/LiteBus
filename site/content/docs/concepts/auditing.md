# Auditing

An audit trail records who acted on whose data, what they did, when, why, and whether it worked. LiteBus produces those records at the mediation boundary from metadata you declare once per message. It records every outcome, including refusals. This page explains the model, the configuration, and what your application must supply.

## Audit Records, Application Logs, and Diagnostics

Before configuring anything, separate three things that are often confused:

| Log | Records | Read by |
| --- | --- | --- |
| Diagnostic log | What the software did | Engineers, during an incident |
| Domain event stream | What became true | The system itself |
| Audit trail | Who is answerable | Auditors, security reviewers, customers |

Deriving the third stream from the second is a tempting shortcut, but it does not work. A domain event carries domain identity rather than use-case identity, and the mapping is not one to one. It carries only what the domain needed, so it may omit the device that acted. Decisively, a domain event only exists when state changed. A refused action changes nothing, yet an audit is most often asked about refusals.

Keep the streams separate. LiteBus does not publish audit records through the outbox for the same reason. The outbox gives at-least-once delivery to other systems, while evidence needs durability at the source.

## Recording at the Mediation Boundary

Writing audit records inside a handler can only record success. Authorization typically refuses before the handler writes its audit record, and a post-handler does not run when the main handler throws. A trail of successes is a changelog, not an audit.

LiteBus writes at the [completion stage](handler-pipeline.md), which runs on every path. It records success, early answer, refusal, failure, and cancellation. The pipeline structure guarantees that every ending is recorded, rather than relying on a rule people have to remember. That stage is also not cancellable, so a cancelled mediation leaves a record instead of dropping the one entry a review would look for.

## Declaring What Is Audited

Every audited message declares the constant half of its record, the parts known without running anything.

Use an attribute when the declaration is a single fact:

```csharp
[Audited("orders.place-order", Category = "money", TargetKind = "order")]
public sealed record PlaceOrderCommand(Guid CartId) : ICommand<OrderId>;
```

Use a [definition](message-definitions.md) when you want real types and shared constants:

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

When both are present, the definition wins.

Messages that are not audited say so, and say why:

```csharp
[AuditExempt("browsing a public storefront is not a sensitive action")]
public sealed record GetStorefrontQuery(Guid StoreId) : IQuery<StorefrontView>;
```

An exemption is a decision, not an omission. The rationale sits beside the message, so a reviewer can see which events were deliberately left out. Auditing standards require event selection to be documented with its justification, and the exemption is that documentation. Keeping the rationale beside the message stops it from drifting away from the code, as a separate document would.

The action code is **use-case identity**, not domain identity. Two call sites that raise the same domain event are two different actions if a person would describe them differently.

### Enforcing That Every Message Declares a Position

Two checks enforce the declaration, and they cover different gaps, so keep both:

```csharp
services.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(messaging => messaging
        .RequireDeclaration<AuditDeclaration, ICommand>()
        .RequireUniqueAuditActions()
        .RequireAuditActionFormat());
});
```

- `LB1018` reports an undeclared command or query at build time. It reads `[Audited]`, `[AuditExempt]`, keyed `IAuditDefinition<TMessage>` definitions, and `Audited`/`NotAudited` calls in a `Describe` body, but only for definitions in the assembly being compiled. It treats a `Describe` body it cannot read as declared. See [Required Declarations](../catalog/analyzers/required-declarations.md).
- `RequireDeclaration<AuditDeclaration, ICommand>()` fails composition for any registered command without a position, whatever shape declared it and whichever assembly holds the definition. `RequireUniqueAuditActions()` fails composition when two messages share an action code. `RequireAuditActionFormat()` fails composition when an action code does not match `MessageModuleBuilder.DefaultAuditActionPattern` (`category.kebab-action`) or the pattern you pass.

The composition checks cover every shape and every assembly. The analyzer reports earlier for the shapes it can see.

## Supplying What Only the Handler Knows

The variable half of a record is known only while the handler runs. A command that creates a resource generates its identifier internally, and a reason composed at runtime cannot be declared in advance. Resolve `IAuditScope` and set the values only the handler knows:

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

The scope holds no state of its own. It reads and writes the ambient execution context, which flows with the mediation and is discarded when it ends. Two concurrent mediations never see each other. Calling one of its methods outside a mediation raises `NoExecutionContextException`, because there is no record for the value to reach.

`IAuditScope` is registered by `AddMessaging`, not by `EnableAuditing()`, so it resolves whether or not an axis produces records. An application that wants the declaration model and its own writer can inject the scope, read declarations through [`IMessageMetadataAccessor`](message-definitions.md#reading-metadata), and never call `EnableAuditing()`. Nothing reads the scope in that configuration, so values a handler sets are discarded. That is the intended behavior, not a misconfiguration the framework should report.

### Declaring What the Message Already Knows

Not every variable value needs the handler. When the target or the reason can be read from the message itself, declare that on the definition and skip the scope call in every handler:

```csharp
public sealed class RefundOrderCommandDefinition : IAuditDefinition<RefundOrderCommand>
{
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.refund-order")
        .WithTargetFrom<RefundOrderCommand>(command => command.OrderId)
        .WithReasonFrom<RefundOrderCommand>(command => command.Justification)
        with { Category = "money", TargetKind = "order", ReasonRequired = true };
}
```

The writer consults the scope first and runs a selector only when the scope holds no value, so a value a handler supplies overrides the selector. For the reason, the pipeline decision sits between the two. A guard denial or an answer explanation describes the ending, while the selector describes the intent, and the ending is what a `Denied` record must explain.

A selector result that is null or blank means the message carries no value, and `ReasonRequired` applies to the final value, so a required reason may come from the scope or the selector alike. A selector that throws fails the record write, the same way a throwing actor resolver does. A projection bug that silently produced evidence without its target or justification would be worse than a failed mediation.

Selectors are delegates, so attributes cannot carry them. A message that reads its target or reason from itself needs a definition. In a `Describe` body, where the `Audited` helper takes only constants, declare the value directly with `declarations.Declare<AuditDeclaration>(AuditDeclaration.Audited("orders.refund-order").WithReasonFrom<RefundOrderCommand>(command => command.Justification))`. This is the shape described under [Declaring a Value Computed From the Message](message-definitions.md#declaring-a-value-computed-from-the-message), where a delegate computes the value from the message. The selector is built once at registration, so it can only project from the message and cannot resolve services.

Some actions are only accountable with a justification. Declare `ReasonRequired = true` and the writer refuses to produce an incomplete record for a successful action, raising `AuditReasonMissingException` instead. A required justification that silently goes missing defeats the requirement, so it is reported rather than recorded as absent.

The throw happens at `HandlerPriorities.Observability`, which is before the commit at `HandlerPriorities.UnitOfWork`, so the work is rolled back rather than standing without its justification. That rollback is the point of the flag. Set it only where you want that trade-off, and supply a reason on every path the handler can return through, either by calling `WithReason` or by declaring a reason selector on the message.

## Audit Configuration

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

The feature has a shared half and a per-axis half. A trail, an actor resolver, and an outcome mapper belong to every axis, and the completion handler that writes a record belongs to each one. `AddAuditing` keeps that from becoming two or three decisions a consumer could make inconsistently. Configuring a trail and selecting no axis fails composition, because no probe can report that at runtime. Nothing is ever audited, so nothing ever fails.

The per-axis switches remain, and `AddAuditing` composes them. Use them directly only when the axes are genuinely configured in separate places:

```csharp
registry.AddMessaging(messaging => messaging.UseAuditTrail<PostgresAuditTrail>());
registry.AddCommands(builder => builder.EnableAuditing());
registry.AddQueries(builder => builder.EnableAuditing());
registry.AddEvents(builder => builder.EnableAuditing());
```

`UseAuditTrail<T>()` lets the container build the trail, so it may take dependencies of its own, and registers it as **scoped**. That lifetime suits a trail wrapping a database session, which is why it is the default.

To use another lifetime, pass it explicitly:

```csharp
registry.AddMessaging(messaging => messaging.UseAuditTrail<HttpAuditTrail>(InstanceLifetime.Singleton));
```

`UseAuditTrailInstance(trail)` registers a trail you already hold. A pre-created instance can only be a singleton, which the method name signals. A trail built there with a database session captures that one session for the life of the process, with no other hint at the call site.

The `litebus.audit.trail` probe reports the lifetime it observes as `trailIsSingleton`. It resolves the trail from two dispatch scopes and compares the instances, which is the only way to see the lifetime from outside the container. A singleton trail wrapping a scoped session raises nothing at startup and misbehaves later under load, so the probe reports the lifetime while the application is still starting.

Registering `IAuditTrail` directly with the application container also works, and the probe accepts either route.

`IAuditTrail` is the only thing you must supply. LiteBus decides when a record is produced and what it contains. Where it is written, and with what durability, is your decision.

An application that authorizes by throwing its own exception type registers an outcome mapper beside the trail, through `UseOutcomeMapper<T>()` on the auditing builder. The mapper records the refusal as `AuditOutcome.Denied` rather than `AuditOutcome.Failed`. An application that denies through a guard or a validator needs no mapper. The pipeline already reports those as decisions, and the code the guard supplied is recorded as the record's `FailureCode`.

### Actor Resolution

`AuditRecord.Actor` is the first column an audit review reads, and the one part of a record LiteBus cannot derive. It knows the action, the outcome, the target, and the time. Who holds the request lives somewhere different in every application, so supply an `IAuditActorResolver`:

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

It runs at the completion stage, which makes it the right extension point rather than a pre-stage handler. A denied command produces a record, and "who tried" is the most useful thing that record can say. A pre-handler never runs when a guard denies, so resolving at the completion stage means attribution survives every path. It covers success, denial, invalid input, failure, and cancellation.

`AuditActor` carries a required `Id` and `Kind`, plus an optional `DisplayName` and `OnBehalfOf`. `Kind` exists so a query can separate the actions people took from the actions a process took. Every review draws that distinction, and an identifier alone cannot express it. It is required because a query cannot use an actor that states no kind. `OnBehalfOf` records a delegated action. It separates support staff acting as a customer from the customer acting, and a device acting on a key from the person who authorized that key.

`Kind` is a string rather than an enumeration, and that is deliberate. The set of things that can act is yours. A service account, a scanner at a door, and a scheduled worker are all actors.

If your own set is closed, keep it closed where you construct the actor. A switch over your own union that returns an `AuditActor` keeps compile-time checking where it helps and leaves the record shape open, which is what a SIEM schema expects. Mapping a closed domain model onto an open record shape takes one expression, and that single expression is the intended shape of the mapping.

### Actor Display Names

`DisplayName` is the field that makes the trail hold personal data. Recording it keeps an entry readable after the account is deleted, but it also stores personal data. Which matters more is yours to decide, so LiteBus never populates it and no factory takes it. Set it with a line of its own:

```csharp
AuditActor.User(account.Id.ToString()) with { DisplayName = account.Email };
```

That line costs the same as an extra argument but forces a deliberate choice. A trail whose stated scope is "no personal data beyond the acting account's identity" is satisfied by leaving it null. If that claim matters, assert it in a test over your resolver. A comment does not fail a build.

### Owning the Record Shape

`AuditRecord` is the handoff to `IAuditTrail`, not a persistence schema. What your store writes down, including its columns and any integrity chaining, is yours to decide. If you also want a different record shape passed to the trail, replace the writer:

```csharp
registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
    .UseRecordWriter<AuditDocumentWriter>()
    .ForAllAxes()));
```

`IAuditRecordWriter.WriteAsync(MessageCompletionContext, CancellationToken)` is the entire contract between the pipeline and auditing. A replacement keeps the completion-stage placement, the observability priority, and the per-axis selection, and replaces exactly the record building. There is no generic `AuditRecord<T>`, deliberately. A type parameter there would spread through five surfaces. Every application wanting the default would pay for the few that do not, and it would add nothing a writer cannot already do.

A replacement owns everything the built-in writer does, and all of it is public:

| What the built-in writer does | What you use |
| --- | --- |
| Skips a message that declares no audit position | `IMessageRegistry.Find`, then `Metadata.TryGet<AuditDeclaration>()` |
| Enforces a required justification | `AuditedDeclaration.ReasonRequired`, `AuditReasonMissingException` |
| Reads the target, reason, properties, and actor a handler supplied, or a selector declared | Inject `IAuditScope`, and read `AuditedDeclaration.TargetSelector` and `AuditedDeclaration.ReasonSelector` |
| Stamps the correlation and tenant identifiers | `AmbientExecutionContext.GetCurrentOrDefault()`, `MessageTraceContextKeys` |
| Classifies the outcome and the failure code | `IAuditOutcomeMapper` |

Skipping an undeclared message is part of that contract rather than a detail. If you leave it out, every message on the selected axes produces a record.

Your writer does not have to use an `IAuditTrail`, so the `litebus.audit.trail` probe stops asserting one is registered and reports the writer instead. LiteBus cannot know what a writer it did not build needs. The composition summary names it too, because a replacement writer owns all of record building, and a misregistration would otherwise look exactly like working auditing.

Returning `null` is legitimate and means nothing established an actor. Do not invent one. A fabricated identifier in evidence is worse than a gap a review can see. When the application knows a process acted, say so with `AuditActor.System`, because a scheduled job and an unattributed action are different answers.

`IAuditScope.WithActor` overrides the resolver for the case only the handler knows, such as an actor established by exchanging a token mid-handler. Attribution that is the same for every message belongs in the resolver, which also covers the paths a handler never reaches.

Without a resolver, records are still written and every one has no actor. The `litebus.audit.trail` probe reports that as `Degraded` rather than unhealthy. A trail that says what happened is worth writing even when it cannot say who is answerable. That limitation should not surface for the first time during a review.

### Auditing Events

`EnableAuditing` exists on the event module too. A domain fact is frequently the thing a review most wants recorded, and it is not always the consequence of an audited command. An event raised by an inbox replay, a scheduled reconciliation, or an integration from another system has no command behind it.

LiteBus writes one record per publish, not one per handler. The mediation is the unit being audited, and the broadcast strategy reports one outcome for the whole publish. A record per subscriber would multiply one fact into as many entries as there are reactions, and the count would change whenever a handler is added.

Turning it on is not a decision about volume. An event that declares no audit position costs one registry lookup and writes nothing, exactly as a command does, so each publish of an explicitly declared event writes one row. The number of rows is a choice you make one event at a time.

The decision worth making carefully is a different one. Auditing both a command and the event it raises records the same business change twice, from two angles. One entry records the action somebody took, and the other records the fact it produced. An event published by a command handler in the same mediation also resolves the same actor, so the two entries agree about who and differ only in wording. That inflates a count a reviewer will trust.

Declare an audit position on the event when it is evidence of something no command records. That means a fact arriving from outside the system, an inbox replay, a reconciliation, or a change with no single commanding action behind it. Declaring both is a choice rather than a mistake, and the action codes keep them apart.

Enabling auditing also registers the `litebus.audit.trail` diagnostic probe, which reports unhealthy when no trail is registered. Without it, a missing trail first surfaces as a fault inside the completion stage, which is the one stage whose faults are deliberately kept away from the caller.

## Denials and Early Answers

The distinction a security review cares about most is whether the actor was permitted. The pipeline carries it. A [guard](handler-pipeline.md) that returns `Deny` reports `MediationOutcome.Denied`, which the audit trail records as `AuditOutcome.Denied` with the reason the guard gave.

A shortcut that answers is a different case. A cache hit or a replayed idempotent command refused nobody, so it is recorded as `AuditOutcome.Succeeded`. Recording it as a denial would add an entry to the denial list for something that never happened. A validation failure is a third case. The message was malformed rather than refused, so it is recorded as `AuditOutcome.Invalid` and stays out of the denial list too.

| Ending | `MediationOutcome` | Recorded as |
| --- | --- | --- |
| The handler ran and post-handlers completed | `Succeeded` | `Succeeded` |
| A shortcut answered without the handler | `Answered` | `Succeeded` |
| A validator reported the message malformed | `Invalid` | `Invalid` |
| A guard refused the message | `Denied` | `Denied` |
| The pipeline threw | `Failed` | `Failed` |
| The caller cancelled | `Canceled` | `Canceled` |

A fault an error handler recovered is still recorded as `Failed`. The outcome records that a fault occurred. The completion context carries no exception, so nothing distinguishes it from a fault that propagated to the caller.

A message mediated through a base descriptor because its own runtime type was never registered is audited under that descriptor's declaration, exactly as if the type had been registered. Whether a mediation leaves a record never depends on scan coverage.

An application that refuses by **throwing** rather than through a guard owns the exception type, so LiteBus cannot classify it. Register a mapper to have that exception recorded as a denial:

```csharp
public sealed class UseCaseAuditOutcomeMapper : IAuditOutcomeMapper
{
    public AuditOutcome Map(MessageCompletionContext context) => context.Outcome switch
    {
        MediationOutcome.Failed when context.Exception is ForbiddenException => AuditOutcome.Denied,
        _ => DefaultAuditOutcomeMapper.MapByOutcome(context)
    };
}

registry.AddMessaging(messaging => messaging.UseAuditOutcomeMapper(new UseCaseAuditOutcomeMapper()));
```

Refusing through a guard or a validator needs no mapper.

## The Record

`AuditRecord` follows the model that NIST SP 800-53 AU-3, PCI DSS Requirement 10, and the DMTF CADF event model all describe. An initiator performs an action on a target, producing an outcome, observed at a time and from a place. Following that shared shape takes no extra design and lets the trail map onto a SIEM schema later without being remodelled.

| Field | Holds |
| --- | --- |
| `Action` | Use-case identity, such as `orders.place-order` |
| `Actor` | Who did it, from the actor resolver or the audit scope |
| `Outcome` | `Succeeded`, `Denied`, `Invalid`, `Failed`, or `Canceled` (an early answer is a success) |
| `OccurredAt`, `Duration` | When it happened and how long it took |
| `Category` | Grouping that drives review and retention |
| `TargetKind`, `TargetId` | What was acted on |
| `Reason` | Why, where the action requires one |
| `FailureCode` | Stable code for a non-success, either the code a guard or validator supplied or the exception type name |
| `MessageType` | For correlating with the diagnostic log |
| `CorrelationId`, `TenantId` | Read from the execution context when present |
| `Properties` | Non-identifying context attached by the handler |

Two things are deliberately absent. The record holds neither the message itself nor any before-and-after snapshot of the changed state. The message is handed to the actor resolver instead, so a payload cannot reach audit storage by default. Handing it to the trail would put the whole payload in front of every trail implementation and make keeping personal data out of audit storage the implementer's discipline rather than the framework's.

A stored payload would turn an audit table into an erasure liability under data-protection law, and it would be redundant, because the domain event stream already records what changed under its own retention rule. The trail records who is answerable and why.

For the same reason, do not put personal data in `Properties`. A trail that holds pseudonymous identifiers can serve an erasure request by breaking the identity mapping, which leaves the records meaningful and the person unidentifiable. A trail full of names cannot serve that request.

## Building the Catalogue from the Declarations

Teams often maintain the catalogue of audited actions, categories, and justification requirements by hand, and the hand-kept copy drifts from the code. That catalogue is a pure function of what the messages declare:

```csharp
var catalog = provider.GetRequiredService<IMessageCatalog>();

foreach (var row in catalog.ToRows())
{
    Console.WriteLine($"{row.Action} ({row.Category}) on {row.TargetKind}");
}

File.WriteAllText("audit-catalogue.md", catalog.ToMarkdown());
```

`IMessageCatalog` resolves at runtime as well as inside a composition check. `ToRows` gives an `AuditCatalogueRow` per audited message, ordered by action so two runs produce the same document. `ToMarkdown` is one formatter over those rows. Rows are the primary output because what a compliance process consumes differs per team. One team wants a wiki page, and another wants a spreadsheet attached to an audit. A library that emitted only Markdown would serve the first and obstruct the second.

An exempt or undeclared message has no row, because this builds a catalogue of audited actions. Enumerate the catalog itself and read `DeclarationExemptions` to report the exemptions and their rationales, which answers a different question and is worth its own document.

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

LiteBus applies your declarations without interpreting them, so it can hand them back but cannot name their columns for you.

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

The commit belongs in the completion stage rather than in a post-handler. A post-handler never runs when the main handler throws, so a commit placed there cannot decide anything about a failure. Everything LiteBus writes afterwards is outside the transaction by construction. The completion stage is the first point that observes how the mediation actually ended.

Plan for two consequences. A record for a refusal or a failure has to be written out of band, as above. That write has to survive whatever caused the failure. Anything your commit handler does after the commit, such as dispatching the domain events the aggregates recorded, runs with `CancellationToken.None`, because the completion stage is not cancellable.

## Application Responsibilities

- Your `IAuditTrail` implementation owns record storage and integrity. A hash chain, an append-only database role, and a retention job are all worth having there rather than in a messaging library.
- Your application owns the transaction boundary. LiteBus does not open, commit, or roll back anything, because only your application defines what its unit of work contains. It does guarantee a position you can commit from, described under [Making a Record Atomic With Its Change](#making-a-record-atomic-with-its-change) above.
- Your trail implementation owns actor capture. LiteBus does not model identity. Capture the acting account from whatever ambient principal your host provides.

## Next

Read [Message Definitions](message-definitions.md) for the declaration mechanism, and [The Handler Pipeline](handler-pipeline.md) for the completion stage this is built on. [In-Process Idempotency](idempotency.md) uses the same declaration model and the same commit position, so the two configure together.
