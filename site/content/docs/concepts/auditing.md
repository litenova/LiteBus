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
public sealed class PlaceOrderCommandAuditDefinition : IAuditDefinition<PlaceOrderCommand>
{
    public AuditDeclaration Audit => AuditDeclaration.Audited(
        AuditActions.Orders.Place,
        category: AuditCategories.Money,
        targetKind: "order");
}
```

When both are present, the definition wins. Keep one definition class per concern: an audit position lives in `{Message}AuditDefinition`, separate from the authorization or idempotency definitions for the same message. The registry tolerates one class implementing several definition interfaces, but that layout mixes unrelated decisions.

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
public sealed class RefundOrderCommandAuditDefinition : IAuditDefinition<RefundOrderCommand>
{
    public AuditDeclaration Audit => AuditDeclaration.Audited(
            "orders.refund-order",
            category: "money",
            targetKind: "order",
            reasonRequired: true)
        .WithTargetFrom<RefundOrderCommand>(command => command.OrderId)
        .WithReasonFrom<RefundOrderCommand>(command => command.Justification);
}
```

Pass `category`, `targetKind`, and `reasonRequired` to `Audited` so the target and reason selectors chain directly on the returned declaration. A `with` expression cannot take a member access without parentheses, so `Audited("...") with { ... }.WithTargetFrom<T>(...)` does not compile. The parenthesized form `(Audited("...") with { ... }).WithTargetFrom<T>(...)` compiles, but the parameters above avoid the `with` expression entirely and match `IMessageDeclarations.Audited(action, category, targetKind, reasonRequired)`.

The writer consults the scope first and runs a selector only when the scope holds no value, so a value a handler supplies overrides the selector. For the reason, the pipeline decision sits between the two. A guard denial or an answer explanation describes the ending, while the selector describes the intent, and the ending is what a `Denied` record must explain.

A selector result that is null or blank means the message carries no value, and `ReasonRequired` applies to the final value, so a required reason may come from the scope or the selector alike. A selector that throws fails the record write, the same way a throwing actor resolver does. A projection bug that silently produced evidence without its target or justification would be worse than a failed mediation.

Selectors are delegates, so attributes cannot carry them. A message that reads its target or reason from itself needs a definition. In a `Describe` body, where the `Audited` helper takes only the constant facts, declare a selector position directly with `declarations.Declare<AuditDeclaration>(AuditDeclaration.Audited("orders.refund-order", category: "money", targetKind: "order", reasonRequired: true).WithReasonFrom<RefundOrderCommand>(command => command.Justification))`. This is the shape described under [Declaring a Value Computed From the Message](message-definitions.md#declaring-a-value-computed-from-the-message), where a delegate computes the value from the message. The selector is built once at registration, so it can only project from the message and cannot resolve services.

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
    public AuditActor? Resolve(MessageCompletionContext context)
    {
        if (context.Message is IActingAccountCommand acting)
        {
            return AuditActor.User(acting.ActingAccountId.ToString());
        }

        var id = context.Identity.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.Identity.Principal?.FindFirst("sub")?.Value;

        if (!string.IsNullOrWhiteSpace(id))
        {
            return AuditActor.User(id);
        }

        return context.Identity.HasTenant
            ? AuditActor.For("tenant", context.Identity.TenantId!)
            : AuditActor.System(ProcessNameOf(context.Message.GetType()));
    }
}
```

It runs at the completion stage, which makes it the right extension point rather than a pre-stage handler. A denied command produces a record, and "who tried" is the most useful thing that record can say. A pre-handler never runs when a guard denies, so resolving at the completion stage means attribution survives every path. It covers success, denial, invalid input, failure, and cancellation.

The context carries the identity the mediation ran for as `MessageCompletionContext.Identity`, copied from `IExecutionContext.Identity` when the completion context is built. The two generic views expose the same value, and a context built by hand carries `ExecutionIdentity.Anonymous` until set. A resolver that attributes from the caller reads the principal or the tenant from there rather than from ambient state, which keeps the attribution rule visible in the resolver signature instead of hiding it in a static read the signature never mentions.

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

Without a resolver, the writer falls back to the principal carried on the completion identity. It reads the identifier from the `NameIdentifier` claim, falling back to the `sub` claim, and reports it as `AuditActor.User` with the display name read from the `Name` claim. When the principal carries no usable identifier claim, the record keeps no actor. That fallback applies only when no resolver is registered: an explicit resolver states the attribution rule, and a principal-derived actor must not silently replace an attribution the resolver deliberately left absent. The `litebus.audit.trail` probe still reports a missing resolver as `Degraded` rather than unhealthy, because the fallback covers only the claims case. A trail that says what happened is worth writing even when it cannot say who is answerable. That limitation should not surface for the first time during a review.

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

## Application Types Named AuditRecord or AuditOutcome

Every audit type lives in `LiteBus.Messaging.Abstractions.Auditing`, so a file imports that namespace to use the framework record. An application with its own audit model still collides, just against the new namespace: its own `AuditRecord` in the same namespace, or its own `AuditOutcome` brought into scope, makes the name ambiguous. Dropping the alias that dodged the old collision breaks those files.

Drop the alias only when nothing else in scope defines the name. When the application defines the name itself, keep the alias and retarget it to the Auditing namespace:

```csharp
using LiteBusAuditRecord = LiteBus.Messaging.Abstractions.Auditing.AuditRecord;
```

That keeps both types usable in one file: the alias names the framework record, and the unqualified name keeps meaning the application type. See [Audit Types Moved to LiteBus.Messaging.Abstractions.Auditing](../migration/v8.md#audit-types-moved-to-litebusmessagingabstractionsauditing).

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

## Auditing Command Batches

When commands run through `ICommandBatchMediator`, each command's record is built exactly as above but buffered instead of written. The audit writer adds the record to the batch's buffer rather than calling `IAuditTrail.WriteAsync`, and every buffered record carries the property `litebus.batch.id` with the batch identifier, a GUID string generated once per batch. That property lets a review group the records one batch produced:

```csharp
var batchId = record.Properties["litebus.batch.id"];
```

A mediation outside a batch writes straight through to the trail, so single-command auditing is unchanged. See [Command batches](../catalog/mediator/command-batches.md).

On commit the buffer is written to `IAuditTrail` in command order, before the batch's single `CommitAsync`. A trail that stages into the same transaction, as the `MartenAuditTrail` above does, stays atomic with the data: the batch's records and the batch's changes commit together.

On rollback the buffer is rewritten before it is written. Each buffered record whose `Outcome` is `AuditOutcome.Succeeded` becomes `AuditOutcome.Failed` with `FailureCode` set to `litebus.batch.rolled_back`, because a success that was rolled back did not happen. The record of the command that stopped the batch keeps its own outcome, whether denial, invalid input, failure, or cancellation. Commands that never ran get no record. The rewritten buffer is written to `IAuditTrail` after `RollbackAsync`:

| Command | Record written on rollback |
| --- | --- |
| A command that passed | `Failed` with `litebus.batch.rolled_back` |
| The command that stopped the batch | Its own outcome and failure code |
| A command that never ran | No record |

A trail that writes into the rolled-back transaction loses these rollback records with the transaction. That matches what happens to a failed single command whose trail rides the transaction being rolled back: the record cannot survive the rollback it describes. When the rollback records must survive, write them out of band, as the `MartenAuditTrail` above writes refusals and failures to its archive.

## Hash-Chained Audit Storage

A record that can be edited after the fact proves nothing. The auditing module can chain its records so a review can prove that no record was removed, reordered, or altered: each record in a segment carries a sequence number and a hash over its own content plus the hash of its predecessor. Altering a record breaks its hash, and recomputing that hash breaks the link the next record carries, so any alteration surfaces at verification no later than the altered record.

Each segment holds one linear chain. LiteBus chains whole segments instead of building a Merkle tree: inclusion proofs serve public logs with untrusted readers, while an in-database trail verifies whole segments and anchors them with checkpoints. Each segment is independent, so retention can expire whole segments while the rest still verify. A chained document carries `Segment`, `Sequence` starting at one, `PreviousHash`, `Hash`, and `HashScheme`. `IAuditChain` operates on the trail the same way on every store: `VerifyAsync`, `VerifyFromAsync`, `CutCheckpointAsync`, `ListSegmentsAsync`, `ListUnpublishedCheckpointsAsync`, `ExpireSegmentAsync`, and `EraseAsync`.

### Audit Package Family

Audit storage and the chain engine live in their own packages, because queries and events are audited as well as commands:

| Package | Role | Holds |
| --- | --- | --- |
| `LiteBus.Messaging.Abstractions` (`Auditing` namespace) | Mediation contracts | Declarations, `IAuditTrail`, `AuditRecord`, `AuditCapture`, `AuditLink`, chain and checkpoint contracts, `IAuditChainStore<TDocument>` |
| `LiteBus.Auditing` | Core implementation | Capture orchestration, linker and sweep, chain engine, streaming verifier, checkpoint lifecycle and publish retrier, framing link rule, hashers, C2SP checkpoint codec, checkpoint policies |
| `LiteBus.Auditing.Storage.Marten` | Feature bridge | Marten audit trail, documents, chain store adapter |
| `LiteBus.Auditing.Storage.PostgreSql` | Feature bridge | Npgsql audit tables, chain store adapter, schema |
| `LiteBus.Auditing.Storage.EntityFrameworkCore` | Feature bridge | EF Core entities, model configuration, chain store adapter |
| `LiteBus.Auditing.Extensions.Microsoft.DependencyInjection`, `LiteBus.Auditing.Extensions.Autofac` | Host adapter | Empty install shells, matching every other module |

### Capture: When a Record Is Written

Capture is when and where the record is written, relative to the business commit. It is a variant record, `AuditCapture`, set on the auditing builder with `Capture` and overridable per message by declaration. The timing decides what a record may truthfully claim:

| Variant | Behavior | What the record claims |
| --- | --- | --- |
| `AuditCapture.WithChange(link)` | Written in the business transaction or session; commits or rolls back with the change | The change happened, atomically true. Default when the audit store can join the business transaction |
| `AuditCapture.AfterCommitThroughOutbox()` | Enqueued through the transactional outbox in the business transaction, delivered to the audit store after commit, at least once, idempotent by record id | The change happened, eventually and reliably. Chosen automatically when the store cannot join the transaction, and listed in the composition model |
| `AuditCapture.BeforeAndOutcome()` | An intent record is written in its own transaction before the handler runs; an outcome record follows when the mediation ends | The command was attempted by the caller; the outcome record states the result. A record written before commit never claims success |

```csharp
auditing.Capture(AuditCapture.WithChange());
auditing.Capture(AuditCapture.WithChange(AuditLink.InTransaction));
auditing.Capture(AuditCapture.AfterCommitThroughOutbox());
auditing.Capture(AuditCapture.BeforeAndOutcome());
```

Denial, invalid, and failure records always take the out-of-band path, in every mode, because there is no business change to commit with and they must survive the rollback. The `BeforeAndOutcome` intent record carries `AuditOutcome.Intent` with zero duration and no failure code.

A message that needs a different timing than the builder default declares one through `IAuditCaptureDefinition<TMessage>` in a definition class beside the message:

```csharp
public sealed class PlaceOrderCommandCaptureDefinition : IAuditCaptureDefinition<PlaceOrderCommand>
{
    public AuditCaptureDeclaration Capture => new(AuditCapture.BeforeAndOutcome());
}
```

There is no attribute form, because an `AuditCapture` is not an attribute-compatible constant. A message with no override is captured with the builder capture, and a message that is not audited ignores the declaration.

### Linking: When a Record Joins the Chain

Linking is when the chain position (sequence, previous hash, hash) is assigned. It applies only with the chain on:

| Linking | Behavior | Cost |
| --- | --- | --- |
| `AuditLink.AfterCommit` | Records commit unlinked; a linker assigns positions in commit order right after commit, with a background sweep for crash leftovers; verification reports an `Unlinked` break past a configured age | No contention on the business path |
| `AuditLink.InTransaction` | The segment head is updated inside the business transaction | Every audited command in a segment contends on one row |
| `AuditLink.AtWrite` | Whoever writes the record links it in its own transaction | Natural when the audit store is a separate system |

Valid combinations: `WithChange` defaults to `AfterCommit` linking and allows `InTransaction`. `AfterCommitThroughOutbox` and `BeforeAndOutcome` link `AtWrite`. Composition rejects any other pair with a `LiteBusConfigurationException`.

The default is `WithChange` with `AfterCommit` linking. Composition picks `AfterCommitThroughOutbox` when the store cannot join the business transaction, and it does so visibly in the composition model, never as a silent downgrade. The `AfterCommit` sweep runs on `AuditLinkSweepHostOptions`: enabled by default, one pass per minute, linking records staged over a minute ago, well before verification reports a record left unlinked past `AuditHashChainOptions.DefaultUnlinkedAfter` (five minutes).

### Enabling Auditing and the Chain

```csharp
services.AddLiteBus(registry =>
{
    registry.AddAuditing(auditing => auditing
        .Capture(AuditCapture.WithChange())
        .UseMartenStore()
        .UseHashChain(chain => chain
            .CheckpointStaleAfter(TimeSpan.FromDays(1))));
});
```

`AddAuditing` registers the auditing module; calling it twice configures one module instead of registering a second one. The store choice (`UseMartenStore`, `UsePostgreSqlStore`, `UseEntityFrameworkCoreStore<TContext>`) records the open-generic trail and chain store the module closes over the configured document type. Register one audit store per composition: registering a second store throws `LiteBusConfigurationException`.

Without `UseHashChain`, auditing writes plain records with no sequence numbers and no hash chain. With it, every piece below has a default, so a chain that needs nothing special writes no further code: `sha256-v2` framing, a checkpoint every 100 records, and an `Unlinked` report past five minutes. The composition model reports the requested and effective capture, the selection reason, the linking mode, and every chain piece under the `auditing` section, so a build can show which capture the store forced and which ports were replaced.

### Chain Pieces and Defaults

Every part of how the chain is formed, hashed, segmented, checkpointed, and verified is a replaceable piece with a LiteBus default, so an application that needs nothing special writes no code and an application with its own retention or signing rules replaces only the pieces it owns:

| Piece | What the application decides | Default |
| --- | --- | --- |
| Piece | What the application decides | Default |
| --- | --- | --- |
| `IAuditRecordMapper<TDocument>` | The stored document: map LiteBus's `AuditRecord` to the application's own type, with `MapAsync` running in the mediation scope before commit | The store default document and mapper (`MartenAuditRecord`, `PostgreSqlAuditRecord`, `EfCoreAuditRecord`) |
| `IAuditSegmenter<TDocument>` | The segment key per record, whether a segment opens with a genesis record, and the head metadata through `DescribeSegment` | Category and year with no genesis record, carrying the tenant and the `category` and `year` attributes |
| `IAuditCanonicalizer<TDocument>` | The exact business bytes hashed: which fields, their order, the encoding, how nulls and times are written | Canonical JSON of every field through the store default canonicalizer |
| `AuditHashScheme<TDocument>` | The named combination of canonicalizer, link rule, and hasher, selected with `UseScheme` | `sha256-v2`: `FramedLinkRule` binding scheme, segment, sequence, and previous hash around the canonical bytes, hashed with `Sha256AuditHasher` |
| `ICheckpointPolicy` | When a checkpoint is cut | Every 100 records through `CheckpointPolicies.EveryRecords(100)` |
| `ICheckpointSigner` | How a checkpoint is signed; every signer signs every checkpoint, so signatures coexist | None |
| `ICheckpointPublisher` | Where checkpoints go; every publisher receives every checkpoint | None beyond the stored checkpoint, which verification reads through the store source |
| `ICheckpointSource` | Which extra checkpoints verification compares against; the store source is always first | The store source only |

LiteBus owns the rest once: conditional appends under the versioned segment head, and the verify, checkpoint, segment, and erasure operations every application needs. Pieces are given as types resolved from the container when the chain runs: `StoreAs<TDocument, TMapper>()`, `SegmentBy<TSegmenter>()`, `SignCheckpointsWith<TSigner>()`, `PublishCheckpointsTo<TPublisher>()`, and `VerifyAgainst<TSource>()`. The write scheme is named with `UseScheme(name)`, the policy is given as an instance or a type with `CheckpointWhen`, and the verification thresholds are values: `UnlinkedAfter(age)`, `CheckpointStaleAfter(age)`, and `PublishErasureNotices()`. Registering the same signer, publisher, or source twice is a no-op. An explicit `StoreAs` or `SegmentBy` call always wins over the store defaults, regardless of call order.

### Storing a Custom Document

An application whose audit model carries fields LiteBus does not know, such as a workspace, a retention class, or a refusal code, stores its own document type. The type implements `IAuditChainDocument`, and the chain writer fills the chain fields when it appends:

```csharp
public sealed class WorkspaceAuditDocument : IAuditChainDocument
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Workspace { get; set; } = string.Empty;
    public string? RefusalCode { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Segment { get; set; }
    public long Sequence { get; set; }
    public string? PreviousHash { get; set; }
    public string? Hash { get; set; }
    public string? HashScheme { get; set; }
}

public sealed class WorkspaceAuditMapper : IAuditRecordMapper<WorkspaceAuditDocument>
{
    public WorkspaceAuditDocument Map(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        record.Properties.TryGetValue("workspace", out var workspace);

        return new WorkspaceAuditDocument
        {
            Id = Guid.NewGuid(),
            Action = record.Action,
            Workspace = workspace ?? "default",
            RefusalCode = record.FailureCode,
            OccurredAt = record.OccurredAt
        };
    }
}
```

`Map` above is enough when mapping needs no services. When it does, such as resolving a pseudonym store, override `MapAsync(record, AuditMappingContext, cancellationToken)` instead: it runs in the mediation scope before commit, with the scope on `AuditMappingContext.Services` and the caller on `AuditMappingContext.Identity`. The default implementation calls `Map`, so a mapper that needs no services keeps working unchanged. The chain writer fills the chain fields afterwards either way.

The segmenter names the segment per record. Returning a document from `CreateGenesis` opens each new segment with a genesis record at sequence one; returning `null` opens segments without one. `DescribeSegment` records the `AuditSegmentInfo` stored on the head: the key, the tenant, the attributes, and when the segment opened. The default implementation records the key with no tenant and no attributes; override it when listing, expiry, or erasure must select segments without parsing keys:

```csharp
public sealed class WorkspaceAuditSegmenter : IAuditSegmenter<WorkspaceAuditDocument>
{
    public string SegmentOf(WorkspaceAuditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return string.Concat(
            document.Workspace,
            "/",
            document.OccurredAt.Year.ToString(CultureInfo.InvariantCulture));
    }

    public WorkspaceAuditDocument? CreateGenesis(string segment, WorkspaceAuditDocument first)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(first);

        return new WorkspaceAuditDocument
        {
            Id = Guid.NewGuid(),
            Action = "audit.segment-opened",
            Workspace = first.Workspace,
            OccurredAt = first.OccurredAt
        };
    }
}
```

A chain that opens segments without a genesis record passes a delegate instead of a segmenter: `SegmentBy<WorkspaceAuditDocument>(document => document.Workspace)`.

The canonicalizer produces the exact bytes hashed. It must exclude `Hash` and include everything else that must be tamper-evident, and it must be deterministic: the same document always yields the same bytes:

```csharp
public sealed class WorkspaceAuditCanonicalizer : IAuditCanonicalizer<WorkspaceAuditDocument>
{
    public byte[] Canonicalize(WorkspaceAuditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var fields = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["action"] = document.Action,
            ["id"] = document.Id.ToString(),
            ["occurred-at"] = document.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
            ["previous-hash"] = document.PreviousHash,
            ["refusal-code"] = document.RefusalCode,
            ["segment"] = document.Segment,
            ["sequence"] = document.Sequence.ToString(CultureInfo.InvariantCulture),
            ["workspace"] = document.Workspace
        };

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fields));
    }
}
```

The scheme names the combination of canonical form, link rule, and hasher. The chain ships two schemes for every document type: `sha256-v1` keeps the pass-through `PreviousHashLinkRule`, so records written before framing verify unchanged, and `sha256-v2` binds the chain position in a `FramedLinkRule`, so the canonicalizer only ever describes the record. Both hash with `Sha256AuditHasher`, and the chain selects the write scheme by name with `UseScheme`. The canonicalizer is resolved per document type from the container: a storage package registers the default for its document as a replaceable LiteBus default, and an application with its own document registers its own. Naming a scheme the registry does not know fails composition, and a scheme whose canonicalizer, link rule or hasher cannot be resolved fails host startup, before any record is written unlinked.

An application that must bind its hashes to a secret uses `HmacSha256AuditHasher`, with a fixed key or with a provider that returns the current key per operation so a rotation takes effect without a restart:

```csharp
IAuditHasher hasher = new HmacSha256AuditHasher(GetAuditKeyAsync);

async ValueTask<byte[]> GetAuditKeyAsync(CancellationToken cancellationToken) =>
    await secretStore.GetAuditKeyAsync(cancellationToken).ConfigureAwait(false);
```

The signer and the publisher are the application's own. The signer owns the key and the scheme, and the publisher decides where checkpoints go, such as an append-only file store:

```csharp
public sealed class HmacCheckpointSigner : ICheckpointSigner
{
    private readonly byte[] _key;

    public HmacCheckpointSigner(byte[] key) => _key = key;

    public ValueTask<AuditCheckpointSignature> SignAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        return ValueTask.FromResult(new AuditCheckpointSignature("hmac-sha256", Compute(checkpoint)));
    }

    public ValueTask<bool> VerifyAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        foreach (var signature in checkpoint.Signatures)
        {
            if (signature.Scheme == "hmac-sha256"
                && CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(signature.Value),
                    Encoding.UTF8.GetBytes(Compute(checkpoint))))
            {
                return ValueTask.FromResult(true);
            }
        }

        return ValueTask.FromResult(false);
    }

    private string Compute(AuditCheckpoint checkpoint)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(
            checkpoint.Segment,
            ":",
            checkpoint.Sequence.ToString(CultureInfo.InvariantCulture),
            ":",
            checkpoint.Hash));

        return Convert.ToHexStringLower(HMACSHA256.HashData(_key, bytes));
    }
}

public sealed class FileCheckpointPublisher : ICheckpointPublisher
{
    private readonly string _directory;

    public FileCheckpointPublisher(string directory) => _directory = directory;

    public async ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var name = string.Concat(
            checkpoint.Segment.Replace("/", "-", StringComparison.Ordinal),
            ".checkpoints.jsonl");

        await File.AppendAllTextAsync(
            Path.Combine(_directory, name),
            JsonSerializer.Serialize(checkpoint) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
    }
}
```

The builder assembles the pieces. Customizing the stored document starts with `StoreAs`, and the segmenter must handle the configured document type:

```csharp
services.AddLiteBus(registry => registry.AddAuditing(auditing => auditing
    .UseMartenStore()
    .UseHashChain(chain => chain
        .StoreAs<WorkspaceAuditDocument, WorkspaceAuditMapper>()
        .SegmentBy<WorkspaceAuditSegmenter>()
        .UseScheme("sha256-v2")
        .CheckpointWhen(CheckpointPolicies.Any(
            CheckpointPolicies.EveryRecords(100),
            CheckpointPolicies.Every(TimeSpan.FromDays(1))))
        .SignCheckpointsWith<HmacCheckpointSigner>()
        .PublishCheckpointsTo<FileCheckpointPublisher>()
        .VerifyAgainst<FileCheckpointSource>())));
```

Every piece is given as a type resolved from the container when the chain runs. The canonicalizer for the custom document is registered separately in the application container, because the scheme registry resolves it per document type. An application that checkpoints on its own rhythm passes `CheckpointPolicies.Never()` and cuts explicitly with `CutCheckpointAsync` instead, so the chain stays silent between explicit cuts.

### Checkpoints and Publication

A hash chain proves internal consistency, but an attacker who rewrites a whole suffix and recomputes every hash still passes verification. Published checkpoints close that gap. A checkpoint anchors the position and hash a segment reached:

| Field | Holds |
| --- | --- |
| `Id` | The checkpoint identifier, stable across publish retries so anchors never diverge |
| `Segment` | The segment the checkpoint covers |
| `Sequence` | The sequence of the last document the checkpoint covers |
| `Count` | The number of documents in the segment at the checkpoint, which proves coverage |
| `Hash` | The hash of the document at `Sequence` |
| `HashScheme` | The scheme that document was written with |
| `Kind` | `Regular` for a checkpoint cut while the segment stays open, `Final` for the last checkpoint of an expired segment |
| `CreatedAt` | When the checkpoint was cut |
| `Label` | The free-form label distinguishing checkpoints cut on different rhythms, or `null` |
| `Signatures` | Every signature over the checkpoint, so a log signature, a timestamp, and witness cosignatures coexist |
| `PublishState` | `Pending` while the checkpoint waits for publication, `Published` once it reached every publisher |
| `PublishAttempts`, `LastPublishError` | How many publish attempts were made, and the last error |

The count is what proves coverage: records 1 through N are all present, not just linked. Verification compares the segment against the latest checkpoint it finds, so a missing record before the head fails the count comparison even when the remaining links are consistent.

A checkpoint is cut, stored as `Pending`, signed, and then published from a background retrier. Publication never runs inside an append. A failed attempt stays pending with the attempt count increased and the error recorded, and `ListUnpublishedCheckpointsAsync` lists what still waits. The retrier runs on `CheckpointPublishHostOptions`: one pass per minute, waiting with backoff after a failed pass instead of spinning. The checkpoint identity stays stable across retries, so anchors never diverge.

Checkpoints render in the C2SP `tlog-checkpoint` signed-note shape, so witnesses, timestamping authorities, and generic verifiers consume them without LiteBus code. The origin line carries the segment, the tree size carries the covered count, the root hash carries the checkpoint hash, and LiteBus fields the shape has no room for travel as extension lines a generic verifier ignores. Each signature renders as one signed-note line.

The chain asks the checkpoint policy after every append. A time-based policy can also become due without a new write, so it reports `IsTimeBased` as `true` and the background service cuts for segments whose policy reports due on its pass too. `CutCheckpointAsync` cuts a checkpoint of a segment now, outside the policy.

### Checkpoint Policies

`CheckpointPolicies` builds the policies a chain cuts with:

| Policy | Due when |
| --- | --- |
| `Never()` | Never; only explicit cuts produce checkpoints |
| `EveryRecords(count, label)` | The segment advanced `count` sequences past its last checkpoint |
| `Every(interval, label)` | The segment holds records and `interval` passed since the last checkpoint |
| `Any(policies)` | Any of the given policies is due |
| `OnSchedule(schedule, label)` | The shared schedule reached its next occurrence |
| `DailyAt(time, zone, label)` / `WeeklyOn(day, time, zone, label)` | Once a day or once a week at a fixed local time, defaulting to UTC |
| `DailyAt(time, zoneOf, label)` / `WeeklyOn(day, time, zoneOf, label)` | Once a day or once a week at a fixed local time read in each tenant zone |

Every factory takes an optional label, stored on the checkpoint a due cut produces, so each signer and publisher can switch on it. The calendar factories default the label to their rhythm (`daily`, `weekly`). The schedule contract (`ISchedule`, `Schedules.Every`/`DailyAt`/`WeeklyOn`, and the per-tenant zone form) lives in a contract package shared with recurring commands, so neither feature depends on the other. A policy that reads services overrides the async `IsDueAsync`; the synchronous `IsDue` stays for callers that evaluate without input and output. An application with its own rhythm implements `ICheckpointPolicy` over `AuditCheckpointCandidate`.

### Anchoring Checkpoints

Where a checkpoint goes is the application decision. LiteBus ships three anchors, each publishing every cut checkpoint and reading it back as a verification source:

| Anchor | Package | Holds |
| --- | --- | --- |
| S3-compatible object storage with Object Lock retention | `LiteBus.Auditing.Anchors.S3` | `S3CheckpointPublisher`, `S3CheckpointSource`, registered with `PublishCheckpointsToS3` |
| Azure Blob Storage with immutability policies and legal hold | `LiteBus.Auditing.Anchors.AzureBlob` | `AzureBlobCheckpointPublisher`, `AzureBlobCheckpointSource`, registered with `PublishCheckpointsToAzureBlob` |
| RFC 3161 timestamping authority | `LiteBus.Auditing.Anchors.Timestamping` | `Rfc3161CheckpointSigner`, registered with `TimestampCheckpoints(authority)`, returning the token as one more signature |

The S3 anchor stores C2SP checkpoint text, so generic verifiers read anchors without LiteBus code. The timestamp signer posts the checkpoint hash to the authority and stores the issued token under the `rfc3161` scheme beside the log signature. See [S3 Checkpoint Anchor](../integrations/auditing-s3-anchor.md), [Azure Blob Checkpoint Anchor](../integrations/auditing-azure-blob-anchor.md), and [RFC 3161 Timestamps](../integrations/auditing-rfc3161-timestamp.md).

### Verifying a Segment

Resolve `IAuditChain` and verify a segment, then read the comparison against its checkpoints:

```csharp
var chain = provider.GetRequiredService<IAuditChain>();
var verification = await chain.VerifyAsync("workspace-a/2026", cancellationToken).ConfigureAwait(false);

if (!verification.IsIntact)
{
    foreach (var @break in verification.Breaks)
    {
        Console.WriteLine($"Sequence {@break.Sequence}: {@break.Kind}: {@break.Detail}");
    }

    return;
}

foreach (var comparison in verification.Comparisons)
{
    Console.WriteLine($"{comparison.SourceName}: hash {comparison.HashMatches}, count {comparison.CountMatches}.");
}
```

Verification streams records by pages, so a large segment verifies without loading it whole. `VerifyFromAsync(segment, checkpointId)` trusts an anchored checkpoint and checks only the records after it, which is how a nightly job verifies from the last anchored checkpoint instead of re-reading the segment from its first record. Every configured source adds one comparison: the store source first, then each registered anchor. A source that cannot be reached is reported with `SourceUnreachable` as a warning, separate from a source that disagrees. The verification stays intact but the gap is visible.

### Verification Results

`VerifyAsync` returns an `AuditChainVerification` that reports every break with its sequence and reason, not only the first:

| Field | Holds |
| --- | --- |
| `Segment` | The verified segment |
| `IsIntact` | Whether the segment has no break |
| `RecordCount` | The number of documents read from the segment |
| `HeadSequence`, `HeadHash` | The last document, or `null` for an empty segment |
| `Breaks` | Every problem found, in sequence order |
| `LatestCheckpoint` | The checkpoint the segment was compared against, or `null` when none exists |
| `MatchesLatestCheckpoint` | Whether the chain matches the latest checkpoint's hash and count, or `null` when there is no checkpoint |
| `CheckpointSignatureValid` | Whether the latest checkpoint's signature is valid, or `null` when no signer is configured or there is no checkpoint |
| `Comparisons` | One comparison per checkpoint source the segment was checked against |

Each break names its sequence, its kind, and a sentence describing the problem for an operator:

| Kind | Meaning |
| --- | --- |
| `Gap` | A sequence number is missing |
| `Duplicate` | A sequence number appears more than once, or out of order |
| `PreviousHashMismatch` | The stored previous hash does not match the hash of the document before it |
| `HashMismatch` | The stored hash does not match the hash recomputed from the document |
| `UnknownHashScheme` | The document names a hash scheme the chain does not accept |
| `CheckpointHashMismatch` | The hash at the latest checkpoint's sequence differs from the checkpoint's hash |
| `CheckpointCountMismatch` | The segment holds fewer documents up to the checkpoint than the checkpoint counted |
| `CheckpointSignatureInvalid` | The latest checkpoint's signature is missing or invalid |
| `CheckpointMissing` | The segment holds no checkpoint although its checkpoint policy would already have cut one (a segment under 100 records with the default policy has none and verifies intact), or the checkpoint `VerifyFromAsync` was asked to trust does not exist |
| `CheckpointStale` | The last checkpoint is older than the configured age while records exist after it |
| `CheckpointUnpublished` | A stored checkpoint still waits for publication |
| `Unlinked` | A record was staged but remains unlinked past the configured age |

`AuditChainVerifier<TDocument>` runs the hash pass without any storage: the caller reads the ordered documents of one segment and its latest checkpoint, and the verifier recomputes every hash with the same canonical form, link rule, and hasher the writer used. Storage adapters for other databases reuse it. The documents must be the whole segment in ascending sequence order: a partial page reports its missing head as gaps, and an unordered list reports the misplaced documents as duplicates.

### Hash Framing and Scheme Changes

Under `sha256-v2`, the link rule hashes a length-prefixed frame (scheme, segment, sequence, previous hash) plus the canonical business bytes. The frame is the scheme name as a four-byte big-endian byte count followed by its UTF-8 bytes, the segment the same way, the sequence as eight bytes big-endian, and the previous hash length-prefixed or the four bytes `FF FF FF FF` with nothing after them when there is no previous hash. The null marker keeps a first record distinct from a record carrying an empty previous hash, and the length prefixes keep every field boundary unambiguous. A consumer that writes its own canonical form over business fields only still gets a linked chain: records cannot be reordered, moved between segments, or replaced without a break.

Each record stores the name of the scheme it was written with, and verification picks the scheme per record, so a chain moves to a new scheme without rewriting history: new records carry the new name, old records keep theirs, and both verify. `sha256-v1` keeps verifying. Records written before schemes existed carry no name and verify unchanged under `sha256-v1`, which carries the same field set the chain always hashed. Two different schemes sharing one name fail composition, because verification could not tell them apart; a deliberate override calls `Replace`.

A composition self-test proves that the write scheme binds the chain position into every hash: it changes the segment, the sequence, and the previous hash of a fixed sample and fails composition when any of them leaves the output unchanged, so a misbound scheme never reaches runtime.

### Segments, Expiry, and Erasure

`AuditSegmentInfo` holds the key, the tenant, the attributes, and when the segment opened, and it is stored on the head, so listing, expiry, and erasure select segments without parsing keys. `ListSegmentsAsync(filter)` lists the segments matching an `AuditSegmentFilter`: one tenant, required attribute values, segments opened before an instant, and whether expired segments are included. The segment head carries the stored identity with the position it reached: the last sequence and hash, the document count, the last checkpoint sequence and time, whether the segment expired, and the version the store checks on a conditional write.

Retention deletes whole expired segments, and verification must survive that. `ExpireSegmentAsync(segment)` cuts a `Final` checkpoint, then deletes every document of the segment and marks the head expired, so the remaining segments still verify and the final checkpoint proves what the deleted segment held:

```csharp
var chain = provider.GetRequiredService<IAuditChain>();
var final = await chain.ExpireSegmentAsync("workspace-a/2024", cancellationToken).ConfigureAwait(false);
```

Expiring an empty segment throws `InvalidOperationException`, because there is nothing to anchor. A second call returns the stored final checkpoint instead of cutting another one, so a retention job stays safe to retry. Appending to an expired segment throws `InvalidOperationException`.

Erasure is a different legal act from expiry: expiry keeps a final checkpoint proving what was removed, while erasure leaves nothing identifying, including anchors that describe the data. `EraseAsync(filter, reason, cancellationToken)` deletes the records, heads, and stored checkpoints matching a filter, and publishes nothing by default:

```csharp
var erased = await chain.EraseAsync(
    new AuditSegmentFilter(TenantId: "workspace-a"),
    "workspace closed",
    cancellationToken).ConfigureAwait(false);
```

An optional erasure notice carries no hash: turn on `PublishErasureNotices` and every registered `IErasureNoticePublisher` receives one `AuditErasureNotice` per erased segment, naming the segment, the document count it held, the reason, and when it was erased. A notice that carried a hash would anchor exactly what must go. A publisher that throws fails the erasure it was called from, so a lost notice surfaces instead of passing silently.

## Application Responsibilities

- Your `IAuditTrail` implementation owns record storage and integrity. A hash chain, an append-only database role, and a retention job are all worth having there rather than in a messaging library.
- Your application owns the transaction boundary. LiteBus never opens or owns the transaction itself: it calls the application's `BeginAsync`, `CommitAsync`, and `RollbackAsync` at defined pipeline points, and the application opens, commits, and rolls back the transaction inside those methods. It does guarantee a position you can commit from, described under [Making a Record Atomic With Its Change](#making-a-record-atomic-with-its-change) above.
- Your trail implementation owns actor capture. LiteBus does not model identity. Capture the acting account through `IAuditActorResolver`, reading the caller from `MessageCompletionContext.Identity`.
- LiteBus does not own checkpoint backends, timestamping authorities, key management for crypto-shredding, retention law, or erasure across the rest of an application data set. It anchors checkpoints where you point it and deletes what its own store holds; the authority, the keys, and the law stay yours.

## Next

Read [Message Definitions](message-definitions.md) for the declaration mechanism, and [The Handler Pipeline](handler-pipeline.md) for the completion stage this is built on. [In-Process Idempotency](idempotency.md) uses the same declaration model and the same commit position, so the two configure together. For the stores, read [Marten Audit Storage](../integrations/auditing-marten.md), [PostgreSQL Audit Storage](../integrations/auditing-postgresql.md), and [Entity Framework Core Audit Storage](../integrations/auditing-entity-framework-core.md). For the anchors, read [S3 Checkpoint Anchor](../integrations/auditing-s3-anchor.md), [Azure Blob Checkpoint Anchor](../integrations/auditing-azure-blob-anchor.md), and [RFC 3161 Timestamps](../integrations/auditing-rfc3161-timestamp.md).
