# In-Process Idempotency

A command declares the key that identifies it, LiteBus claims that key before the handler runs, and a repeat is answered without running the work again. This page is about commands mediated in the caller's process. The durable inbox and outbox have their own idempotency, described in [Reliable Messaging Semantics](../reliable-messaging/semantics.md).

Read [Message Definitions](message-definitions.md) first. Idempotency is declared the same way an audit position is.

## Library Support for Idempotency

Every application that mediates a command twice rediscovers the same three things. The key has to come from the message, because two deliveries of one intent are the same message and nothing else about them is stable. The claim has to be atomic, and the only reliable way to get that is to let the storage engine refuse the duplicate rather than reading first and writing second. A failed attempt has to release the key, or a transient error turns the retry into a false repeat.

The shortcut stage is where the answer lives: a declaration states the key, and a store contract states where the key is remembered.

## Declaring the Key

```csharp
public sealed class ApplyPaymentCommandDefinition : IIdempotencyDefinition<ApplyPaymentCommand>
{
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<ApplyPaymentCommand>(command => command.PaymentId.ToString());
}
```

The selector is a pure projection over the message: it reads fields and formats them. It runs once per mediation and cannot resolve services, because a declaration is created once at registration. When deriving the key needs services, declare `KeyedByContext()` and let a guard supply it.

The key alone cannot tell the same request from a different request reusing the key. An HTTP client that retries a payment with the same idempotency key but a changed amount is not repeating the first request, and answering it from the stored record would report the wrong payment as done. `WithFingerprint` projects a fingerprint from the message content alongside the key:

```csharp
public sealed class ApplyPaymentCommandDefinition : IIdempotencyDefinition<ApplyPaymentCommand>
{
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<ApplyPaymentCommand>(command => command.PaymentId.ToString())
            .WithFingerprint<ApplyPaymentCommand>(command => $"{command.Amount}:{command.Currency}");
}
```

The first delivery stores the fingerprint with its claim. A repeat with a matching fingerprint is answered as already applied. A repeat with a different one is denied with `idempotency.fingerprint-mismatch`, so the caller learns the key is taken rather than receiving another request's answer. Without a fingerprint, any repeat of the key is answered as already applied.

## Keys That Need Services

Some keys cannot be projected from the message. A processor webhook carries a raw body and a signature and nothing else, deliberately, so that nothing unverified reaches a decision. Its deduplication key is the provider's event id, which can only be known after the signature is checked and the body is parsed. That is a lookup, and a lookup needs services.

Declare the message with `KeyedByContext()` and resolve the key in a guard. The pre stage runs guards before shortcuts, so the key is present when the idempotency shortcut reads it:

```csharp
public sealed class ProcessorWebhookCommandDefinition : IIdempotencyDefinition<ProcessorWebhookCommand>
{
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedByContext() with { Scope = "processor-webhook" };
}

public sealed class VerifyProcessorWebhookGuard : ICommandGuard<ProcessorWebhookCommand>
{
    private readonly IProcessorWebhookVerifier _verifier;
    private readonly IExecutionContext _context;

    public VerifyProcessorWebhookGuard(IProcessorWebhookVerifier verifier, IExecutionContext context)
    {
        _verifier = verifier;
        _context = context;
    }

    public async Task<Verdict> DecideAsync(
        ProcessorWebhookCommand message,
        CancellationToken cancellationToken = default)
    {
        var verified = await _verifier.VerifyAsync(message.RawBody, message.SignatureHeader, cancellationToken);

        if (verified is null)
        {
            return Verdict.Deny("the webhook signature did not verify");
        }

        _context.Data.Set(new SuppliedIdempotencyKey(verified.EventId));
        return Verdict.Allow;
    }
}
```

The key is read once and reused by both the shortcut that claims it and the completion handler that settles it, so the expensive verification happens once per mediation rather than once per stage. This is why the key is supplied rather than resolved through a container-registered provider. A provider would be invoked by each stage independently, paying the cost twice and, if it were not perfectly deterministic, writing a different key at each stage.

A message declared this way that reaches the shortcut with no key in context raises `IdempotencyConfigurationException` naming the message type. The pipeline reports this misconfiguration instead of substituting a generated key. A generated fallback would make every delivery of the same intent look distinct, so the store would accept them all and the declaration would appear to work while doing nothing.

| Member | Meaning |
| --- | --- |
| `KeySelector` | Projects the key from the message. Required. |
| `FingerprintSelector` | Projects the content fingerprint from the message. Absent by default; set it with `WithFingerprint`. |
| `Scope` | Prefixes the key. Defaults to the message full type name, so two message types never collide by accident, including two types with one simple name and two closed generics. Set it when two types share a key space on purpose. A blank scope raises `IdempotencyConfigurationException`. |
| `ReplayResult` | Whether a repeat replays the result the first attempt produced. Off by default. |

A blank key raises `IdempotencyConfigurationException` rather than being used. Every message with a blank key shares one key space, so the first would answer all the others.

The mediation tenant prefixes the stored key when the mediation carries one. Set `CommandMediationSettings.Items[MessageTraceContextKeys.TenantId]` from the caller context before sending. Two integrations set it for you: inbox replay copies the stored envelope tenant, and the ASP.NET Core filter resolves it through `TenantResolver` (see [ASP.NET Core Idempotency Key](../integrations/aspnetcore-idempotency-key.md)). Without a tenant scope, two tenants sending one key string for one command share a store row, and one tenant's repeat is answered with the other's stored result whenever the fingerprints match or either side carries none. Tenant, scope, and key segments are escaped before they are joined, so no two distinct triples produce one stored key.

## Enabling Idempotency

```csharp
services.AddLiteBus(registry =>
{
    registry.AddMessaging(_ => { });

    registry.AddCommands(builder =>
    {
        builder.RegisterFromAssembly(typeof(ApplyPaymentCommand).Assembly);
        builder.EnableIdempotency();
    });
});
```

That registers two shortcuts and one completion handler, all of which ignore a command declaring nothing, so one call covers the axis and only the declaring commands use them. The shortcut stage runs after guards and validators, so an unauthorized or malformed command cannot claim a key.

You supply the store, or register the shipped PostgreSQL one with `commands.UsePostgreSqlIdempotencyStore(...)` from `LiteBus.Commands.Storage.PostgreSql` (see [PostgreSQL Idempotency Store](../integrations/commands-postgresql-idempotency-store.md)). The `litebus.idempotency.store` probe reports `Unhealthy` when idempotency is enabled and no `IIdempotencyStore` is registered.

## Writing the Store

```csharp
public interface IIdempotencyStore
{
    Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default);
    Task CompleteAsync(string key, string claimToken, string? payload, CancellationToken cancellationToken = default);
    Task ReleaseAsync(string key, string claimToken, CancellationToken cancellationToken = default);
}
```

Two rules decide whether the store is correct.

1. Claim by insert, not by read. `TryClaimAsync` must be atomic, and the way to get that is to insert the key and treat a primary-key violation as the refusal. Reading first and writing second loses the race idempotency exists to win, because two concurrent deliveries both read nothing and both proceed.

2. Claim inside the transaction that applies the change. A key claimed in one transaction and a change written in another can come apart. A crash between them leaves a key claimed for work that never happened, and the retry is answered as already applied. Write the claim through the same unit of work the handler writes through and let it commit both. See [Auditing](auditing.md#making-a-record-atomic-with-its-change) for where that commit goes. The shape is the same, and the completion handler that settles the claim runs at `HandlerPriorities.Persistence`, which is before an application's commit at `HandlerPriorities.UnitOfWork`.

Four answers cover what a claim can meet. `Granted` runs the message and `AlreadyCompleted` answers the repeat from the stored record. `InFlight` refuses a delivery whose key another delivery still holds, and `FingerprintMismatch` refuses a repeat whose fingerprint differs from the stored one. Both end the mediation as `Denied`, so `TrySendAsync` returns the denial and `SendAsync` raises `LiteBusMessageDeniedException`. Anything else is a broken store, and the shortcut reports it as `PipelineContractException` naming the store rather than running the handler without a recorded claim.

Every grant carries a claim token fenced into the stored row. The completion handler settles with the token the shortcut was granted, and the store completes or releases only while the stored claim still carries it. A settle that arrives after a claim-timeout takeover is a no-op. A late completion cannot mark the new claimant's work applied, a late release cannot free its live claim, and completing a key the caller does not hold stores nothing.

The denial carries a machine-readable code from `IdempotencyRefusalCodes`: `idempotency.in-flight` for a claim still running, `idempotency.fingerprint-mismatch` for a key reused with different content. An HTTP boundary maps them the way the IETF Idempotency-Key header draft does. Return 409 Conflict for in-flight, telling the client to retry after the first delivery settles, and 422 Unprocessable Content for a mismatch, telling the client the request itself must change because the key already identifies other content.

Nothing expires an applied key. A store that grows forever is a storage problem with a storage answer: a retention job. Only you know how long a repeat is still plausible.

An unsettled claim is different. A claimant that crashes without settling would block its key until an operator intervenes, so a store takes the claim over for a new caller once it is older than the store's claim timeout, five minutes by default on every shipped store. Keep the timeout longer than the slowest mediation that claims through the store, so a live claimant always settles first. The timeout must be positive. Zero or negative is rejected when the store is built, because it would make every unsettled claim immediately takeover-eligible.

`InMemoryIdempotencyStore` in `LiteBus.Testing.Mediation` is a test double, and it is shipped from the testing package on purpose. It forgets everything on restart, and a second process behind a load balancer shares nothing with the first, so both apply the same message. Idempotency is a property of the whole system, and a per-process store cannot provide it.

The double still answers like a durable store. A held key reports in-flight until its claim outlasts `ClaimTimeout`, and an applied key compares fingerprints. Pass a controlled `TimeProvider` and a short timeout to drive the takeover deterministically in tests.

## Commands That Produce a Result

A repeat can only be answered with a value if the first attempt recorded one. Set `ReplayResult` and the first attempt hands the serialized result to the store to keep:

```csharp
public sealed class SettlePaymentCommandDefinition : IIdempotencyDefinition<SettlePaymentCommand>
{
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<SettlePaymentCommand>(command => command.PaymentId.ToString())
            with { ReplayResult = true };
}
```

Without it, a repeated result-producing command raises `PipelineContractException` naming the fix. LiteBus will not invent an answer. Returning `default` would hand the caller a zero or a null that looks like a real result. A command declared with `KeyedByContext` that replays results should also set a fingerprint with `WithFingerprint`. Without one, any repeat of the key is answered with the stored result, even a different request reusing a predictable key.

Turn replay on for a command whose caller needs the same answer twice, such as an endpoint a client retries. Leave it off for a redelivered command nobody is waiting on, and for a command with no result, where there is nothing to replay.

## What Happens on Each Path

| Mediation outcome | Effect on the key |
| --- | --- |
| `Succeeded` | Marked applied, with the serialized result when `ReplayResult` is set |
| `Answered` | Released when this mediation claimed the key before a later shortcut answered; left alone when the shortcut answered a repeat without claiming |
| `Denied`, `Invalid` | Left alone: a guard or validator denial never claimed, and an in-flight or mismatch denial holds no claim either, so there is nothing to settle |
| `Failed`, `Canceled` | Released, so the retry runs |

Only the mediation that was granted the key settles it. The shortcut records each granted key in execution-context data, and the completion handler completes or releases that recorded key. A mediation that never claimed, because it was denied, invalid, answered as a repeat, or denied as in-flight or mismatched, settles nothing, so it can never delete another delivery's in-flight claim. The record carries the token fencing the grant, and the completion handler settles with it without re-running the key or fingerprint selectors, so a handler that mutates the message cannot turn its own success into a resolution failure.

Release is what makes a transient failure survivable. Keeping the key claimed after a database timeout would make the retry a false repeat, which is the opposite of what idempotency is for.

## Keying an Outbox Message by the Command's Key

When a command handler enqueues follow-up events, those envelopes can share the command's idempotency key instead of inventing their own. The durable `Idempotency.FromCommandKey` (in `LiteBus.Messaging.Abstractions.DurableMessaging`) reads the key the shortcut granted the in-flight command and returns it as durable `Keyed` metadata. Pass a suffix when one command enqueues several events so each envelope keys distinctly.

```csharp
public async Task HandleAsync(ApplyPaymentCommand message, CancellationToken cancellationToken = default)
{
    var receipt = await _outbox.EnqueueAsync(
        OutboxEnqueueItem<PaymentAppliedEvent>.From(
            new PaymentAppliedEvent { PaymentId = message.PaymentId },
            OutboxEnqueueMetadata.Immediate with
            {
                Idempotency = Idempotency.FromCommandKey("applied"),
            }),
        cancellationToken).ConfigureAwait(false);
}
```

This is explicit and opt-in. Nothing reads the granted key unless the handler calls `FromCommandKey`. The call throws `InvalidOperationException` outside a keyed command's mediation, either because the command declares no idempotency key or because no mediation scope exists, so a key is never invented silently.

## Next

Read [The Handler Pipeline](handler-pipeline.md) for the shortcut and completion stages behind this feature, and [Handler Priority](handler-priority.md#the-reserved-framework-window) for how the claim settlement is ordered against your commit.
