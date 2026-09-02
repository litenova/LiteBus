# In-Process Idempotency

A command declares the key that identifies it, LiteBus claims that key before the handler runs, and a repeat is answered without running the work again. This page is about commands mediated in the caller's process. The durable inbox and outbox have their own idempotency, described in [Reliable Messaging Semantics](../reliable-messaging/semantics.md); this is the same problem one layer in.

Read [Message Definitions](message-definitions.md) first. Idempotency is declared the same way an audit position is.

## Why It Belongs in the Library

Every application that mediates a command twice rediscovers the same three things. The key has to come from the message, because two deliveries of one intent are the same message and nothing else about them is stable. The claim has to be atomic, and the only reliable way to get that is to let the storage engine refuse the duplicate rather than reading first and writing second. And a failed attempt has to release the key, or a transient error turns the retry into a false repeat.

The shortcut stage was already the right shape for the answer. What was missing was a declaration for the key and a contract for where it is remembered.

## Declaring the Key

```csharp
public sealed class ApplyPaymentCommandDefinition : IIdempotencyDefinition<ApplyPaymentCommand>
{
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<ApplyPaymentCommand>(command => command.PaymentId.ToString());
}
```

The selector is a pure projection over the message: read fields, format them. It runs once per mediation and cannot resolve services, because a declaration is created once at registration. Anything needing a lookup belongs in a guard, which can hand its result forward through [`IExecutionContext.Data`](execution-context.md).

| Member | Meaning |
| --- | --- |
| `KeySelector` | Projects the key from the message. Required. |
| `Scope` | Prefixes the key. Defaults to the message type name, so two message types never collide by accident. Set it when two types share a key space on purpose. |
| `ReplayResult` | Whether a repeat replays the result the first attempt produced. Off by default. |

A blank key raises `LiteBusConfigurationException` rather than being used. Every message with a blank key shares one key space, so the first would answer all the others.

## Enabling It

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

That registers two shortcuts and one completion handler, all of which ignore a command declaring nothing, so one call covers the axis and only the declaring commands pay for it. The shortcut stage runs after guards and validators, so an unauthorized or malformed command cannot claim a key.

You supply the store. The `litebus.idempotency.store` probe reports `Unhealthy` when idempotency is enabled and no `IIdempotencyStore` is registered.

## Writing the Store

```csharp
public interface IIdempotencyStore
{
    Task<IdempotencyClaim> TryClaimAsync(string key, CancellationToken cancellationToken = default);
    Task CompleteAsync(string key, string? payload, CancellationToken cancellationToken = default);
    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);
}
```

Two rules decide whether the store is correct.

**Claim by insert, not by read.** `TryClaimAsync` must be atomic, and the way to get that is to insert the key and treat a primary-key violation as the refusal. Reading first and writing second loses the race idempotency exists to win, because two concurrent deliveries both read nothing and both proceed.

**Claim inside the transaction that applies the change.** A key claimed in one transaction and a change written in another can come apart: a crash between them leaves a key claimed for work that never happened, and the retry is answered as already applied. Write the claim through the same unit of work the handler writes through and let it commit both. See [Auditing](auditing.md#making-a-record-atomic-with-its-change) for where that commit goes; the shape is the same, and the completion handler that settles the claim runs at `HandlerPriorities.Persistence`, which is before an application's commit at `HandlerPriorities.UnitOfWork`.

There are only two claim outcomes, not three. A delivery still being applied is not a state the pipeline can do anything sensible with: answering the caller would report work done that might still fail, and proceeding would apply it twice. A store facing a concurrent claim either waits for the other transaction to settle, which a primary-key insert does for free, or throws its own conflict exception for the caller to retry.

Nothing expires a key. A store that grows forever is a storage problem with a storage answer, a retention job, and only you know how long a repeat is still plausible.

`InMemoryIdempotencyStore` in `LiteBus.Testing.Mediation` is a test double, and it is shipped from the testing package on purpose. It forgets everything on restart, and a second process behind a load balancer shares nothing with the first, so both apply the same message. Idempotency is a claim about the system, and a per-process store cannot make it.

## Commands That Produce a Result

A repeat can only be answered with a value if the first attempt recorded one. Set `ReplayResult` and the store is handed the serialized result to keep:

```csharp
public sealed class SettlePaymentCommandDefinition : IIdempotencyDefinition<SettlePaymentCommand>
{
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<SettlePaymentCommand>(command => command.PaymentId)
            with { ReplayResult = true };
}
```

Without it, a repeated result-producing command raises `LiteBusConfigurationException` naming the fix. LiteBus will not invent an answer: returning `default` would hand the caller a zero or a null that looks like a real result.

Turn replay on for a command whose caller needs the same answer twice, such as an endpoint a client retries. Leave it off for a redelivered command nobody is waiting on, and for a command with no result, where there is nothing to replay.

## What Happens on Each Path

| Mediation outcome | Effect on the key |
| --- | --- |
| `Succeeded` | Marked applied, with the serialized result when `ReplayResult` is set |
| `Answered` | Left alone. The shortcut answered because the key was already applied, so there is no claim of its own to settle |
| `Denied`, `Invalid`, `Failed`, `Canceled` | Released, so the retry runs |

Release is what makes a transient failure survivable. Burning the key on a database timeout would make the retry a false repeat, which is the opposite of what idempotency is for.

## Next

Read [The Handler Pipeline](handler-pipeline.md) for the shortcut and completion stages this is built on, and [Handler Priority](handler-priority.md#the-reserved-framework-window) for how the settle is ordered against your commit.
