# Tenancy

A tenancy position states whether a message runs inside a tenant, runs across tenants under a stated reason, or belongs to no tenant. LiteBus carries that position from the message definition to the message metadata, where one guard enforces it for every message, and propagates the ambient tenant onto durable envelopes, idempotency keys, audit records, and store sessions. This page explains the three positions, how to require every message to declare one, and how the tenant flows from mediation to storage.

## Tenancy Position Contents

Every message of a multi-tenant application is in one of three positions. A tenant-scoped command requires a tenant. A retention or integrity job runs across tenants and records why. Sign-in belongs to no tenant and records why:

```csharp
[TenantRequired]
public sealed record PostManualEntryCommand(Guid WorkspaceId, EntryDraft Draft) : ICommand;

[CrossTenant("the retention job purges expired entries across every workspace")]
public sealed record PurgeExpiredEntriesCommand(DateOnly Before) : ICommand;

[NoTenant("sign-in creates the session, so there is no tenant yet")]
public sealed record SignInCommand(string Email, string Password) : ICommand<SessionToken>;
```

A cross-tenant or tenantless position is a decision, not an omission. Because the rationale sits beside the message, a reviewer can see which messages were deliberately placed outside one tenant. It also lets the declaration requirement distinguish a considered position from a message nobody looked at. A missing tenant check is a data leak between customers, so an undeclared message must never read as an approved position during a review.

Use a definition class when you want real types and shared constants:

```csharp
internal sealed class PostManualEntryCommandTenancyDefinition
    : ITenancyDefinition<PostManualEntryCommand>
{
    public TenancyDeclaration Tenancy => TenancyDeclaration.Required();
}
```

One definition class implements one concern. An application that declares authorization, audit, and tenancy keeps three classes beside the message:

```csharp
internal sealed class PostManualEntryCommandDefinition
    : IAuthorizationDefinition<PostManualEntryCommand>,
      IAuditDefinition<PostManualEntryCommand>,
      ITenancyDefinition<PostManualEntryCommand>
{
    public TenancyDeclaration Tenancy => TenancyDeclaration.Required();
    // Authorization and Audit as today
}
```

The registry tolerates one class implementing several definition interfaces, but that layout mixes unrelated decisions. Prefer `PostManualEntryCommandTenancyDefinition` beside the authorization and audit definitions for the same message.

A position is a `TenancyDeclaration`, and you read it back through [`IMessageMetadataAccessor`](message-definitions.md#reading-metadata) whichever shape declared it. The three positions are separate types: `TenantRequiredDeclaration`, `CrossTenantDeclaration` carrying the reason, and `NoTenantDeclaration` carrying the reason. Because they are separate types, a declaration cannot hold an invalid combination of positions. Build them with `TenancyDeclaration.Required()`, `TenancyDeclaration.CrossTenant(reason)`, and `TenancyDeclaration.None(reason)`.

When both an attribute and a definition declare the same message, the definition wins, because both contribute the same `TenancyDeclaration` and the definition is applied last. See [Message Definitions](message-definitions.md) for the mechanism all shapes share, including family defaults and precedence.

## Requiring a Position

A declaration works only when every message states one. A command that forgets to state its position looks exactly like a command that needs no tenant, so allowing undeclared messages turns that omission into an unscoped use case. Require the declaration at composition time, and register the guard in the same call:

```csharp
registry.AddMessaging(messaging => messaging
    .UseTenancy()
    .RequireDeclaration<TenancyDeclaration>());
```

`UseTenancy` registers `TenancyGuard<TMessage>` once as an open generic, so one call covers every message. `RequireDeclaration<TenancyDeclaration>()` fails composition for any registered message without a position, whatever shape declared it and whichever assembly holds the definition. Composition fails with an error that names every offending message rather than only the first.

Scope the requirement when the rule is about some messages only:

```csharp
registry.AddMessaging(messaging => messaging
    .RequireDeclaration<TenancyDeclaration, ICommand>());
```

Scope the requirement on a type rather than a namespace. A namespace is a string that a refactoring tool moves without telling anyone, so a requirement keyed on one silently stops applying when a folder is renamed. For a tenancy rule, that failure leaves a command unscoped.

### Custom Composition Checks

`RequireDeclaration` asserts that every message states a position. `ValidateComposition` asserts anything else your conventions require over the same catalog, and runs at the same point, after every module has built, so the catalog holds every message the host composed:

```csharp
messaging.ValidateComposition(catalog =>
{
    var tenantless = catalog
        .Where(entry => typeof(IBillingCommand).IsAssignableFrom(entry.MessageType)
            && (!entry.Metadata.TryGet<TenancyDeclaration>(out var declaration)
                || declaration is not TenantRequiredDeclaration))
        .Select(entry => entry.MessageType.Name)
        .ToList();

    if (tenantless.Count > 0)
    {
        throw new InvalidOperationException(
            $"Billing commands must require a tenant: {string.Join(", ", tenantless)}");
    }
});
```

Throw from the callback to fail composition, and name every offender in one message. A check that reports the first problem makes a team fix a convention one restart at a time.

## Enforcing It in the Guard

The guard reads the declaration for the runtime message type and checks it against the tenant of the mediation in flight. A message declaring a required tenant is denied with the `tenancy.required` code (`TenancyRefusalCodes.TenantRequired`) when the execution identity has no tenant. A message declaring a cross-tenant or tenantless position is allowed, because it already stated it runs outside one tenant.

A message that declares nothing is allowed, so an application adopts tenancy declaration by declaration. Pair the guard with the requirement above so composition fails for a message that states no position, rather than the omission passing the guard as an allowed message nobody considered. A denied message is refused before the handler runs, and the denial is recorded like any other mediation outcome. An HTTP boundary answers it with `403 Forbidden`. See [ASP.NET Core Execution Identity](../integrations/aspnetcore-execution-identity.md).

Guards that need the caller read it from the same identity the tenancy guard checks. `AuthorizationGuard<TMessage>` exposes it as `CurrentIdentity`, which returns `ExecutionIdentity.Anonymous` outside a mediation. See [Execution Context](execution-context.md) for the resolution order.

### Guard Order

The authorization guard runs before the tenancy guard. `PolicyAuthorizationGuard<TMessage>` carries `[HandlerPriority(HandlerPriorities.Authorization)]` (`ReservedFloor + 10`) and `TenancyGuard<TMessage>` carries `[HandlerPriority(HandlerPriorities.Tenancy)]` (`ReservedFloor + 20`), and both values are stable across releases. A signed-out caller on a tenant route therefore receives the authorization refusal (401) before the tenancy one, and an anonymous `POST` that requires sign-in answers `authorization.not-signed-in` rather than `tenancy.required`. An application that wants the tenant checked before a permission puts a tenant check inside its authorization policy instead of reordering the guards.

### One Code for Two Missing-Tenant Cases

`TenancyGuard` denies with the single code `tenancy.required` whether the caller is anonymous or signed in without a tenant. The guard cannot tell the two apart from the identity alone, and neither can a refusal mapper without re-reading the principal. Keep the single code and split the cases with a sign-in check first: the authorization guard or the endpoint answers a principal-less caller with its own sign-in code and status (401), and only a signed-in caller without a tenant reaches the tenancy denial (403).

Composition requires the declaration, but nothing checks that every dispatch path can supply a tenant. A tenant-required message dispatched from a path with no ambient identity, such as a worker without an explicit identity or a recurring factory that builds a bare command, fails only at runtime with a denial when that path runs. Pass an explicit identity on those paths with `CommandMediationSettings.ForTenant(tenantId)` so the tenant is present before the guard runs.

## Ambient Tenant on Durable Envelopes

Envelopes staged inside a tenant should carry that tenant without each call site repeating it. `UseAmbientTenant` on the outbox and inbox builders stamps the tenant of the ambient execution identity onto envelopes whose items carry no explicit tenant, beside `UseAmbientTrace`:

```csharp
builder.AddOutbox(outbox =>
{
    outbox.UseAmbientTrace();
    outbox.UseAmbientTenant();
    // storage + dispatch...
});

builder.AddInbox(inbox =>
{
    inbox.UseAmbientTrace();
    inbox.UseAmbientTenant();
    // storage + dispatch...
});
```

The precedence is fixed. An explicit `TenantScope.Isolated` on the item always wins. When the item is `TenantScope.Unscoped` and the option is enabled, the envelope factory stamps the tenant of `AmbientExecutionContext.GetCurrentOrDefault()?.Identity`. Outside a mediation the ambient read yields none, so the envelope stays unscoped, identical to one written without the option. Trace and tenant follow the same rule independently: an explicit item value wins, the ambient value fills an absent one, and no ambient mediation leaves the envelope as the item described it.

The in-process dispatchers restore the tenant on the way out. `EventOutboxDispatcher` and `CommandInboxDispatcher` set `Identity = ExecutionIdentity.ForTenant(envelope.TenantId)` on the mediation settings they build, so a reaction runs in the tenant of the fact that caused it. Only the tenant is restored. A principal is never persisted on an envelope.

## Tenant-Aware Stores

The tenant joins the idempotency key scope. When the execution identity has a tenant, `IdempotencyKeyResolver` stores the claim under `"{tenantId}/{key}"` instead of the unscoped key, so two tenants using one key string for one command hold separate store rows. Key segments are escaped before they are joined, so no two distinct tenant, scope, and key tuples produce one string. When the identity carries no tenant, the trace tenant entry is read instead, which is where the ASP.NET Core idempotency filter puts the tenant its resolver returned. Stores are unchanged: the scoping happens in the key the pipeline claims.

This changes the persisted key for tenant-scoped claims. A claim stored before the upgrade under the unscoped key is not found under the tenant-prefixed key after it. Treat the upgrade as breaking for multi-tenant idempotent commands.

Audit records carry the tenant the same way. `AuditRecord.TenantId` is filled from the trace tenant entry when present, and from `IExecutionContext.Identity.TenantId` when the trace items carry no tenant. When no `IAuditActorResolver` is registered, the writer resolves the actor from the identity principal instead: the `ClaimTypes.NameIdentifier` claim, else the `sub` claim, as `AuditActor.User(id)` with `DisplayName` from the `ClaimTypes.Name` claim. No principal gives a `null` actor as before. An explicit resolver always wins over the principal fallback. See [Auditing](auditing.md).

Outbox leasing stays fair across tenants. `OutboxProcessorOptions.MaxLeasedPerTenant` caps how many envelopes of one tenant a single lease call returns, so one busy tenant cannot fill every batch while others wait. Unscoped envelopes count as one tenant. The default is `null`, which leases without a cap. The processor carries the value to the store as `OutboxLeaseRequest.MaxPerTenant`, and every outbox store honors it. Envelopes past the cap stay pending and are leased by a later call, in the store's usual order. See [Security and Tenancy](../operations/security-and-tenancy.md).

Document stores filter by the ambient tenant without application code at the call site. `UseMartenTenantSessions` registers `MartenTenantSessionFactory`, which opens a Marten session for the tenant the current mediation runs in. See [Marten Unit of Work, Idempotency, and Audit](../integrations/commands-marten.md).

## Application Responsibilities

- Whether a message runs inside a tenant is your declaration. LiteBus enforces the position and propagates the tenant, but only your team knows which use cases are tenant-scoped.
- Where the ambient tenant comes from is your host's decision. Register an `IExecutionIdentityProvider`, or use the ASP.NET Core provider that reads the principal and a tenant claim from the current request.
- Which tenant an explicit item names is your call site's decision. `UseAmbientTenant` fills an absent tenant; it never overrides one you set.
- What a tenant means to your stores is your schema's decision. Marten conjoined tenancy, per-tenant processors, and tenant-filtered queries each assume the envelope carries the right tenant.

## Next

Read [Execution Context](execution-context.md) for the identity the guard checks and the order it resolves in, and [Authorization Declarations](authorization-declarations.md) for the declaration this one mirrors. [Security and Tenancy](../operations/security-and-tenancy.md) covers processor isolation and per-tenant lease fairness for operators.
