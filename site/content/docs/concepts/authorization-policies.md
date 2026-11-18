# Authorization Policies

An authorization declaration states what a message requires, and a policy decides whether the caller satisfies it. Before this model, each product wrote its own policy engine for the same checks: signed-in, permission, claim, recent sign-in, system, and ownership. Those checks now live in LiteBus once, as combinators over one evaluation shape, with a permission port and a condition contribution point for the parts that stay in the application. This page explains the checks, how to combine and register them, and the order they run in.

## Selecting a Policy From a Declaration

A message selects its policy by declaring a name:

```csharp
[Authorized("vaults.open")]
public sealed record OpenVaultCommand(Guid VaultId) : ICommand;
```

The composition registers a `MessagePolicy` under that name, and the guard evaluates it. Names compare ordinally: a policy name is vocabulary the composition owns, and two names differing only by case are two policies. A message that declares a name no composition registered is denied with the `authorization.unknown-policy` code rather than passing a message whose rule is missing. See [Authorization Declarations](authorization-declarations.md) for the declaration shapes that carry the name.

## The Evaluation Input

Every check reads one `AuthorizationContext`, built once per guard decision:

```csharp
public sealed record AuthorizationContext(
    object Message,
    Type MessageType,
    ExecutionIdentity Identity,
    IServiceProvider Services,
    DateTimeOffset HandledAt);
```

`Message` is the message being mediated and `MessageType` is its runtime type. `Identity` is who the mediation runs for: the calling principal and the tenant the work belongs to. `Services` is the mediation's dispatch scope, not the application root, so a permission lookup resolved from it observes the same scoped services the handlers do. `HandledAt` is the instant the mediation started, read once from the configured time provider. Every contributed condition and the named policy observe the same instance, so each stage of one decision reads the same message, identity, and instant.

## The Built-In Checks

Each check is a static factory on the abstract `MessagePolicy` record. A check the factories cannot express is a subclass that overrides `EvaluateAsync`; the guard treats a custom policy exactly like a built one, so the composition vocabulary and the evaluation order stay in one place whatever the checks are.

`SignedIn()` allows a signed-in caller and denies anyone else. Signed in means the mediation identity carries an authenticated principal; presence of a principal object alone does not count, because an anonymous request still carries one with an unauthenticated identity. A denial carries `authorization.not-signed-in`, so a boundary can answer it with an authentication challenge rather than a refusal.

`Permission(string permission)` allows when the caller holds a permission, read through the registered `IPermissionLookup`. With none registered, the check denies with `authorization.permission-lookup-missing` rather than guessing. When the lookup answers `false`, the denial carries `authorization.missing-permission`.

`Claim(string type, string? value = null)` allows when the principal carries a claim of the given type, and of the given value when one is passed. Type and value compare ordinally. A caller that is not signed in denies with `authorization.not-signed-in`; a signed-in caller without the claim denies with `authorization.forbidden`.

`RecentSignIn(TimeSpan maxAge)` allows when the last sign-in is recent. It reads the `auth_time` claim, in Unix seconds, and compares it against the handled instant of the mediation. A caller that is not signed in denies with `authorization.not-signed-in`; a signed-in caller with no usable sign-in instant, or one older than `maxAge`, denies with `authorization.forbidden`. The claim carries whole seconds, so a sign-in stamped in the same second the mediation started has an age of zero.

`System()` allows a system run and denies any call with a principal. A system run is a mediation whose identity carries no principal, such as a background reaction or a recurring job. A call with a principal denies with `authorization.forbidden`.

`Self<TMessage>(Func<TMessage, string?> subjectId)` allows when the caller is the subject the message names. The principal's identifier is its `ClaimTypes.NameIdentifier` claim, else its `sub` claim, which is the same order the audit actor resolution reads, and the identifiers compare ordinally. A caller that is not signed in denies with `authorization.not-signed-in`. A message of another type, a message naming no subject, a principal carrying no identifier, and an identifier that differs all deny with `authorization.forbidden`: ownership that cannot be confirmed is refused rather than guessed.

## Combining Checks

`All(params MessagePolicy[])` allows when every inner policy allows, evaluated in order. The first deny wins and evaluation stops there, so a later policy never overrides an earlier refusal. An empty conjunction allows.

`Any(params MessagePolicy[])` allows when any inner policy allows, evaluated in order. The first allow wins and evaluation stops there. When nothing allows, the first deny is returned, so the refusal names the earliest requirement the caller missed. An empty disjunction denies with `authorization.forbidden`.

`When(Func<AuthorizationContext, bool> condition, MessagePolicy inner)` applies an inner policy only when a condition holds. When the condition does not hold, the policy allows without evaluating the inner policy, because a rule that does not apply must not refuse.

`Allow()` always allows. It is the branch of a `When` that needs no check, and the placeholder a composition holds until the real rule is written. `Deny(string reason)` always denies with `authorization.forbidden`. It is how a composition closes a message while stating, in its own words, what to do instead.

```csharp
policies.Add("vaults.open", MessagePolicy.All(
    MessagePolicy.SignedIn(),
    MessagePolicy.Permission("vaults.open")));

policies.Add("vaults.close", MessagePolicy.Any(
    MessagePolicy.Permission("vaults.close"),
    MessagePolicy.When(
        static context => context.Identity.HasTenant,
        MessagePolicy.Self<VaultCommand>(static command => command.VaultId.ToString()))));
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

## Registering Policies

Register names, conditions, and the lookup together on the messaging builder:

```csharp
registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
    .Add("vaults.open", MessagePolicy.All(MessagePolicy.SignedIn(), MessagePolicy.Permission("vaults.open")))
    .AddCondition<BusinessHoursCondition>()
    .UsePermissionLookup<RolePermissionLookup>()));
```

`Add` registers a policy under the name messages declare. Each name is registered once: two policies under one name would leave the guard guessing which one the message meant, so the second registration fails composition naming the duplicate. `AddCondition<TCondition>` registers a condition the guard constructs from the dispatch scope; conditions run in registration order, so register the cheapest, most general condition first. `UsePermissionLookup<TLookup>` registers the lookup permission checks read through; only one lookup serves a composition, so calling it twice keeps the last call.

The call registers `PolicyAuthorizationGuard<TMessage>` once as an open generic, so it covers every message, including messages registered later by the axis modules. Call `AddAuthorizationPolicies` once per composition: a second call fails composition because the policy set is already registered.

## Evaluation Order

One authorized message runs one fixed order: every registered condition in registration order, then the policy registered under the declared name, with the first deny winning. Exempt and undeclared messages never reach the policies. An exemption allows in the base guard, and a message declaring nothing is denied with the `declaration.missing` code, because a use case nobody considered must not pass a check nobody wrote for it.

## Refusal Codes

| Code | Reported when |
| --- | --- |
| `authorization.not-signed-in` | The check requires a signed-in caller and the mediation has none |
| `authorization.missing-permission` | The caller does not hold the permission the policy requires |
| `authorization.forbidden` | A signed-in caller fails a claim, ownership, sign-in age, or system check |
| `authorization.unknown-policy` | The message declares a policy name no composition registered |
| `authorization.permission-lookup-missing` | A permission check runs with no `IPermissionLookup` registered |

The first four are public constants on `AuthorizationPolicyRefusalCodes`. Each is carried on the denial refusal, so `TrySendAsync` returns them and the exception `SendAsync` raises exposes them as its code. An HTTP boundary switches on them: a caller that is not signed in is asked to authenticate (401), while a signed-in caller that lacks the permission is refused (403).

## Next

Read [Authorization Declarations](authorization-declarations.md) for the positions that select these policies, [Message Definitions](message-definitions.md) for the declaration mechanism, and [Execution Context](execution-context.md) for the identity and handled instant the checks read.
