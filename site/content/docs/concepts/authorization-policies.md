# Authorization Policies

An authorization declaration states what a message requires, and a policy decides whether the caller satisfies it. Before this model, each product wrote its own policy engine for the same checks: signed-in, permission, claim, recent sign-in, system, and ownership. Those checks now live in LiteBus once, as combinators over one evaluation shape, with a permission port and a condition contribution point for the parts that stay in the application. This page explains the checks, how to combine and register them, and the order they run in.

## Selecting a Policy From a Declaration

A message selects its policy by declaring a name:

```csharp
[Authorized("vaults.open")]
public sealed record OpenVaultCommand(Guid VaultId) : ICommand;
```

The composition registers a `MessagePolicy` under that name, and the guard evaluates it. Names compare ordinally: a policy name is vocabulary the composition owns, and two names differing only by case are two policies. A composition that calls `AddAuthorizationPolicies` fails at startup for a name a message declares that no call registered, with a `LiteBusConfigurationException` that names every unknown policy name and the messages that use it, so a typo fails once where it was written instead of denying every request. Left to mediation, the same typo would deny with the `authorization.unknown-policy` code. `MessagePolicyRegistry.PolicyNames` exposes the registered names, so a `ValidateComposition` check can assert over declared policies without restating them. See [Authorization Declarations](authorization-declarations.md) for the declaration shapes that carry the name.

## The Evaluation Input

Every check reads one `AuthorizationContext`, built once per guard decision:

```csharp
public sealed record AuthorizationContext(
    object Message,
    Type MessageType,
    ExecutionIdentity Identity,
    IServiceProvider Services,
    DateTimeOffset HandledAt)
{
    public IExecutionContext? ExecutionContext { get; init; }
}
```

`Message` is the message being mediated and `MessageType` is its runtime type. `Identity` is who the mediation runs for: the calling principal and the tenant the work belongs to. `Services` is the mediation's dispatch scope, not the application root, so a permission lookup resolved from it observes the same scoped services the handlers do. `HandledAt` is the instant the mediation started, read once from the configured time provider. `ExecutionContext` is the mediation's execution context, set by the guard from the ambient execution context, so a policy can read the enclosing mediation the message arrived through. Every contributed condition and the named policy observe the same instance, so each stage of one decision reads the same message, identity, and instant.

## The Built-In Checks

Each check is a static factory on the abstract `MessagePolicy` record. A check the factories cannot express is a subclass that overrides `EvaluateAsync`; the subclass must itself be a record, because only records may inherit from a record. A class fails to compile, so write a record with an explicit constructor. The guard treats a custom policy exactly like a built one, so the composition vocabulary and the evaluation order stay in one place whatever the checks are.

`SignedIn()` allows a signed-in caller and denies anyone else. Signed in means the mediation identity carries an authenticated principal; presence of a principal object alone does not count, because an anonymous request still carries one with an unauthenticated identity. A denial carries `authorization.not-signed-in`, so a boundary can answer it with an authentication challenge rather than a refusal.

`Tenant()` allows when the mediation identity carries a tenant and denies otherwise with the `tenancy.required` code. It lets an application order sign-in, tenant, and permission checks inside one policy, as in `All(SignedIn(), Tenant(), Permission("vaults.open"))`, so a signed-out caller is asked to authenticate (401) before a tenant-less caller is refused (403). See [Evaluation Order](#evaluation-order) for the guard-level order this complements.

`Permission(string permission)` allows when the caller holds a permission, read through the registered `IPermissionLookup`. With none registered, the check denies with `authorization.permission-lookup-missing` rather than guessing. When the lookup answers `false`, the denial carries `authorization.missing-permission`.

`Claim(string type, string? value = null)` allows when the principal carries a claim of the given type, and of the given value when one is passed. Type and value compare ordinally. A caller that is not signed in denies with `authorization.not-signed-in`; a signed-in caller without the claim denies with `authorization.forbidden`.

`RecentSignIn(TimeSpan maxAge)` allows when the last sign-in is recent. It reads the sign-in instant from the registered `ISignInInstantSource` when one is present, else from the `auth_time` claim, in Unix seconds, and compares its age against the handled instant of the mediation as a `TimeSpan`. The overload `RecentSignIn(TimeSpan maxAge, string claimType)` reads the named claim instead of `auth_time` when no source is registered. A caller that is not signed in denies with `authorization.not-signed-in`; a signed-in caller whose sign-in is older than `maxAge` denies with `authorization.forbidden`; a signed-in caller with a missing or unparsable sign-in instant denies with `authorization.sign-in-instant-unknown`. See [Reading Sign-In Instants Through a Port](#reading-sign-in-instants-through-a-port).

`System()` allows a system run and denies any call with a principal. A system run is a mediation whose identity carries no principal, such as a background reaction or a recurring job. A call with a principal denies with `authorization.forbidden`. A principal-less identity may still carry a tenant: system work inside a tenant runs with `ExecutionIdentity.For(null, tenantId)`, which passes `System()` and the tenancy position together. See [System Work Inside a Tenant](#system-work-inside-a-tenant).

`Self<TMessage>(Func<TMessage, string?> subjectId)` allows when the caller is the subject the message names. The principal's identifier is its `ClaimTypes.NameIdentifier` claim, else its `sub` claim, which is the same order the audit actor resolution reads, and the identifiers compare ordinally. A caller that is not signed in denies with `authorization.not-signed-in`. A message of another type, a message naming no subject, a principal carrying no identifier, and an identifier that differs all deny with `authorization.forbidden`: ownership that cannot be confirmed is refused rather than guessed.

`Condition<TCondition>()` evaluates one authorization condition where the policy names it. It resolves `TCondition` from the dispatch scope and answers its verdict, so the check runs only for the policies that name it, unlike a condition registered with `AddCondition`, which runs for every authorized message. Register the type with `RegisterCondition<TCondition>()` on the policy builder; when the type is not registered, the policy denies with `authorization.condition-missing` rather than guessing.

`SentBy<TParent>()` allows when the enclosing mediation handles a message assignable to `TParent`, read from `AuthorizationContext.ExecutionContext.Parent.MessageType`. A root mediation, with no enclosing mediation, denies, as does any other caller, with `authorization.not-sent-by`. It admits a named caller without granting it the permission itself, as in `Any(Permission("vaults.open"), SentBy<ExportArchiveQuery>())`.

## Combining Checks

`All(params MessagePolicy[])` allows when every inner policy allows, evaluated in order. The first deny wins and evaluation stops there, so a later policy never overrides an earlier refusal. An empty conjunction allows.

`Any(params MessagePolicy[])` allows when any inner policy allows, evaluated in order. The first allow wins and evaluation stops there. When nothing allows, the first deny is returned, so the refusal names the earliest requirement the caller missed. An empty disjunction denies with `authorization.forbidden`.

`When(Func<AuthorizationContext, bool> condition, MessagePolicy inner)` applies an inner policy only when a condition holds. When the condition does not hold, the policy allows without evaluating the inner policy, because a rule that does not apply must not refuse.

`Public()` always allows. It is the permanent policy for public routes such as sign-in start and token refresh, where there is no caller to authorize yet. `Allow()` also always allows, but it is the branch of a `When` that needs no check, and the placeholder a composition holds until the real rule is written. Use `Public()` where the message is public by design, so a reviewer does not read it as an unfinished rule.

`Deny(string reason)` always denies with `authorization.forbidden`. It is how a composition closes a message while stating, in its own words, what to do instead.

`WithRefusal(string code, string? reason = null)` wraps a policy so a denial carries the given code and, when one is passed, the given reason. An allow passes through unchanged; a denial keeps its decision and carries the replacement code with the replacement reason, else the inner reason. It lets an application keep its own refusal codes on built-in combinators, as in `MessagePolicy.Permission("vaults.open").WithRefusal("vaults.no-access", "the vault is sealed for stocktake")`.

```csharp
policies.Add("vaults.open", MessagePolicy.All(
    MessagePolicy.SignedIn(),
    MessagePolicy.Permission("vaults.open")));

policies.Add("vaults.close", MessagePolicy.Any(
    MessagePolicy.Permission("vaults.close"),
    MessagePolicy.When(
        static context => context.Identity.HasTenant,
        MessagePolicy.Self<VaultCommand>(static command => command.VaultId.ToString()))));

policies.Add("vaults.seal", MessagePolicy.All(
    MessagePolicy.SignedIn(),
    MessagePolicy.Tenant(),
    MessagePolicy.Permission("vaults.seal")));
```

## Reading Permissions Through a Port

`IPermissionLookup` is the port a permission check reads through:

```csharp
ValueTask<bool> HasPermissionAsync(
    ExecutionIdentity identity,
    string permission,
    AuthorizationContext context,
    CancellationToken cancellationToken);
```

LiteBus carries the permission name from the policy to the lookup and never interprets it: whether `vaults.open` is a role, a scope, or a row in a permissions table stays in the application. Register one implementation when a composition uses `MessagePolicy.Permission`:

```csharp
public sealed class RolePermissionLookup : IPermissionLookup
{
    public ValueTask<bool> HasPermissionAsync(
        ExecutionIdentity identity,
        string permission,
        AuthorizationContext context,
        CancellationToken cancellationToken)
    {
        return new ValueTask<bool>(identity.Principal?.IsInRole(permission) == true);
    }
}
```

## Reading Sign-In Instants Through a Port

`ISignInInstantSource` is the port a recent sign-in policy reads through:

```csharp
ValueTask<DateTimeOffset?> GetSignInInstantAsync(
    AuthorizationContext context,
    CancellationToken cancellationToken);
```

Register one implementation when the sign-in instant lives outside the token claims, such as in a server-side session record. With none registered, the policy reads the `auth_time` claim instead, or the claim type the `RecentSignIn` overload names. A source that treats an unknown instant as allowed returns `context.HandledAt`, which makes the sign-in age zero:

```csharp
public sealed class SessionSignInInstantSource : ISignInInstantSource
{
    public ValueTask<DateTimeOffset?> GetSignInInstantAsync(
        AuthorizationContext context,
        CancellationToken cancellationToken)
    {
        var stamped = context.Identity.Principal?.FindFirst("session_start")?.Value;

        return new ValueTask<DateTimeOffset?>(long.TryParse(stamped, out var unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : null);
    }
}
```

## Contributing Conditions

An `IAuthorizationCondition` is a check one feature contributes that runs for every authorized message before the named policy:

```csharp
ValueTask<Verdict> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken);
```

Conditions are how a module adds its own authorization rule without owning the policy vocabulary. A tenancy module denies a tenant-scoped message evaluated outside its tenant; a maintenance module denies writes while a migration runs. Each registers one condition, and the policy guard runs every registered condition, in registration order, before the policy the message declares. The first deny wins, so a condition never overrides another check that already refused.

```csharp
public sealed class BusinessHoursCondition : IAuthorizationCondition
{
    public ValueTask<Verdict> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        var hour = context.HandledAt.Hour;

        return new ValueTask<Verdict>(hour is >= 9 and < 17
            ? Verdict.Allow
            : Verdict.Deny("writes outside business hours need an on-call approval"));
    }
}
```

A condition observes the same `AuthorizationContext` the named policy does. It must stay free of effects a caller would not want from a "may I" question, because the guard stage also runs when the pipeline is asked to evaluate a message without performing it.

A condition that belongs to one policy rather than every message is named by that policy with `Condition<TCondition>()` and registered with `RegisterCondition<TCondition>()`, which registers the type as itself, scoped, without making it global:

```csharp
registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
    .Add("invoicing.sales-invoice-access", MessagePolicy.Condition<SalesInvoiceAccessCondition>())
    .RegisterCondition<SalesInvoiceAccessCondition>()));
```

Use `AddCondition` for a rule that applies to every authorized message and `Condition` with `RegisterCondition` for a rule one policy owns, so messages that never name it never pay for it.

## Registering Policies

Register names, conditions, and the ports together on the messaging builder:

```csharp
registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
    .Add("vaults.open", MessagePolicy.All(MessagePolicy.SignedIn(), MessagePolicy.Permission("vaults.open")))
    .AddCondition<BusinessHoursCondition>()
    .UsePermissionLookup<RolePermissionLookup>()));
```

`Add` registers a policy under the name messages declare. Each name is registered once: two policies under one name would leave the guard guessing which one the message meant, so the second registration fails composition naming the duplicate. `AddCondition<TCondition>` registers a condition the guard constructs from the dispatch scope; conditions run in registration order, so register the cheapest, most general condition first. `RegisterCondition<TCondition>` registers a policy-scoped condition type without making it global. `UsePermissionLookup<TLookup>` registers the lookup permission checks read through; only one lookup serves a composition, so a second call naming another type fails composition, while repeating the same type is harmless. `UseSignInInstantSource<TSource>` registers the source recent sign-in policies read through, with the same one-type rule.

The call registers `PolicyAuthorizationGuard<TMessage>` once as an open generic, so it covers every message, including messages registered later by the axis modules. The call is additive: each assembly in a modular monolith contributes its own `AddAuthorizationPolicies` call, and every call configures the same policy set. The guard, the build step, and the composition check are registered once, on the first call. A duplicate policy name still fails, and a second `UsePermissionLookup` or `UseSignInInstantSource` naming another type fails, so two assemblies cannot silently disagree about which port serves the composition.

```csharp
// From another assembly: adds to the same set rather than replacing it.
registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
    .Add("vaults.seal", MessagePolicy.Permission("vaults.seal"))));
```

## Events Publish Without a Declaration

Events are facts, not requests: publishing an event notifies subscribers that something already happened, so it asks nothing of the caller. The event module declares a default `AuthorizationExemptDeclaration` for the `IEvent` scope, so a composition with authorization policies publishes undeclared events, including events replayed from durable storage. An explicit per-event declaration still applies, because a declaration written closer to the message overrides the family default. A composition that already states its own `IEvent` authorization default keeps it: the duplicate is the application's explicit choice, not a conflict.

```csharp
// No declaration needed: the event module default exempts it.
public sealed record VaultOpenedEvent(Guid VaultId) : IEvent;

// An explicit position still applies where an event needs one.
[Authorized("vaults.audit")]
public sealed record VaultSealedEvent(Guid VaultId) : IEvent;
```

## System Work Inside a Tenant

System work runs with a principal-less identity that may still carry a tenant. `ExecutionIdentity.For(null, tenantId)` describes a background run scoped to one tenant: it passes `MessagePolicy.System()`, because no principal is present, and it passes `MessagePolicy.Tenant()` and the tenancy guard, because a tenant is present. A blank tenant maps to the anonymous identity, so only a real tenant scopes the run:

```csharp
var settings = new CommandMediationSettings
{
    Identity = ExecutionIdentity.For(principal: null, tenantId: "tenant-a")
};

await mediator.SendAsync(new PurgeExpiredEntriesCommand(before), settings, cancellationToken)
    .ConfigureAwait(false);
```

Pair the identity with `TenancyDeclaration.Required()` on the message and a `System()` policy, and the mediation passes both guards together: the tenancy guard sees the tenant, and the authorization guard sees no principal. Without the tenant, the same mediation is denied with `tenancy.required`.

## Evaluation Order

One authorized message runs one fixed order: every registered condition in registration order, then the policy registered under the declared name, with the first deny winning. Exempt and undeclared messages never reach the policies. An exemption allows in the base guard, and a message declaring nothing is denied with the `declaration.missing` code, because a use case nobody considered must not pass a check nobody wrote for it. Undeclared events are exempt by the event module default, so they publish.

The authorization guard runs before the tenancy guard. `PolicyAuthorizationGuard<TMessage>` carries the `HandlerPriorities.Authorization` priority and `TenancyGuard<TMessage>` carries `HandlerPriorities.Tenancy`, both stable across releases, so a signed-out caller sees the authorization refusal (401) before the tenancy one, whatever order the two calls were registered in. An application that checks the tenant before a permission puts `Tenant()` inside its policy, as in `All(SignedIn(), Tenant(), Permission("vaults.open"))`, instead of reordering the guards. See [Security and Tenancy](../operations/security-and-tenancy.md) for the operational view of this order.

## Refusal Codes

| Code | Reported when |
| --- | --- |
| `authorization.not-signed-in` | The check requires a signed-in caller and the mediation has none |
| `authorization.missing-permission` | The caller does not hold the permission the policy requires |
| `authorization.forbidden` | A signed-in caller fails a claim, ownership, sign-in age, or system check |
| `authorization.unknown-policy` | The message declares a policy name no composition registered |
| `authorization.permission-lookup-missing` | A permission check runs with no `IPermissionLookup` registered |
| `authorization.condition-missing` | A policy names a condition type the dispatch scope did not register |
| `authorization.sign-in-instant-unknown` | The sign-in instant is missing or unparsable, so a recent sign-in cannot be confirmed |
| `authorization.not-sent-by` | The enclosing mediation does not handle the message type the policy requires |
| `tenancy.required` | A `Tenant()` policy runs while the mediation identity has no tenant |

All but the last are public constants on `AuthorizationPolicyRefusalCodes`; `tenancy.required` is `TenancyRefusalCodes.TenantRequired` in `LiteBus.Messaging.Abstractions.Tenancy`. Each is carried on the denial refusal, so `TrySendAsync` returns them and the exception `SendAsync` raises exposes them as its code. An HTTP boundary switches on them: a caller that is not signed in is asked to authenticate (401), while a signed-in caller that lacks the permission is refused (403).

## Next

Read [Authorization Declarations](authorization-declarations.md) for the positions that select these policies, [Message Definitions](message-definitions.md) for the declaration mechanism, and [Execution Context](execution-context.md) for the identity and handled instant the checks read.
